using RIE.Core.Properties;
using RIE.Core.Roblox;

namespace RIE.Core.Watch;

public enum WatchEventKind { Change, RuleOn, RuleOff, RuleHit }

public sealed record WatchEvent(DateTimeOffset Timestamp, string Path, string Property, string? OldValue, string? NewValue,
    WatchEventKind Kind = WatchEventKind.Change, string? Rule = null, double? DurationMs = null)
{
    public override string ToString() => Kind switch
    {
        WatchEventKind.RuleOn => $"{Timestamp:HH:mm:ss.fff}  ▶ RULE {Rule} ON  ({Path}.{Property} = {NewValue ?? "(none)"})",
        WatchEventKind.RuleOff => $"{Timestamp:HH:mm:ss.fff}  ■ RULE {Rule} ENDED after {DurationMs:0} ms",
        WatchEventKind.RuleHit => $"{Timestamp:HH:mm:ss.fff}  ● RULE {Rule}  {Path}.{Property}: {OldValue ?? "(none)"} -> {NewValue ?? "(none)"}",
        _ => $"{Timestamp:HH:mm:ss.fff}  {Path}  {Property}: {OldValue ?? "(none)"} -> {NewValue ?? "(none)"}",
    };
}

/// <summary>
/// Watches paths (relative to DataModel, e.g. "Players.User.PlayerGui.Main.Label") by polling.
/// Per path: Exists, ClassName, ChildCount, and properties from <see cref="PropertyReader"/>.
/// Instance addresses are cached and validated (name + class) every poll; on mismatch the path is re-resolved.
/// Rules: level conditions produce RuleOn/RuleOff (with duration); Changed produces RuleHit.
/// </summary>
public sealed class WatchService(InstanceReader reader, PropertyReader properties)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, string?>> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Addr, string Name, string Cls)> _cache = new(StringComparer.Ordinal);
    private readonly List<string> _paths = new();
    private readonly List<WatchRule> _rules = new();
    private readonly Dictionary<WatchRule, DateTimeOffset> _active = new();
    private readonly List<WatchEvent> _log = new();

    public int MaxLog { get; set; } = 20_000;
    /// <summary>Numeric history for sparklines.</summary>
    public WatchSeries Series { get; } = new();
    /// <summary>Log rule events only (plain changes are still counted but not logged).</summary>
    public bool RulesOnly { get; set; }
    public IReadOnlyList<string> Paths { get { lock (_lock) return _paths.ToArray(); } }
    public IReadOnlyList<WatchRule> Rules { get { lock (_lock) return _rules.ToArray(); } }
    public IReadOnlyList<WatchEvent> Log { get { lock (_lock) return _log.ToArray(); } }

    public void Add(string path) { lock (_lock) if (!_paths.Contains(path)) _paths.Add(path); }
    public void Remove(string path)
    {
        lock (_lock)
        {
            _paths.Remove(path); _last.Remove(path); _cache.Remove(path);
            Series.Remove(path);
            foreach (var r in _rules.Where(r => r.Path == path).ToList()) { _rules.Remove(r); _active.Remove(r); }
        }
    }
    public void AddRule(WatchRule rule) { lock (_lock) { _rules.Add(rule); if (!_paths.Contains(rule.Path)) _paths.Add(rule.Path); } }
    public void RemoveRule(WatchRule rule) { lock (_lock) { _rules.Remove(rule); _active.Remove(rule); } }
    public void ClearLog() { lock (_lock) _log.Clear(); }

    public List<WatchEvent> Poll(long dataModel, DateTimeOffset? now = null)
    {
        var ts = now ?? DateTimeOffset.Now;
        var events = new List<WatchEvent>();
        string[] paths; WatchRule[] rules;
        lock (_lock) { paths = _paths.ToArray(); rules = _rules.ToArray(); }
        var samples = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var path in paths) samples[path] = Sample(dataModel, path);
        foreach (var (path, s) in samples) Series.Record(path, s);

        lock (_lock)
        {
            foreach (var path in paths)
            {
                var current = samples[path];
                _last.TryGetValue(path, out var prev);
                foreach (var k in (prev?.Keys ?? Enumerable.Empty<string>()).Union(current.Keys))
                {
                    string? ov = null;
                    prev?.TryGetValue(k, out ov);
                    current.TryGetValue(k, out var nv);
                    if (string.Equals(ov, nv, StringComparison.Ordinal)) continue;
                    if (!RulesOnly) events.Add(new WatchEvent(ts, path, k, ov, nv));
                    if (prev is not null)
                        foreach (var r in rules)
                            if (r.Op == RuleOp.Changed && r.Path == path && (r.Property == k || r.Property == "*"))
                                events.Add(new WatchEvent(ts, path, k, ov, nv, WatchEventKind.RuleHit, RuleName(r)));
                }
                _last[path] = current;
            }
            foreach (var r in rules)
            {
                if (r.Op == RuleOp.Changed || !samples.TryGetValue(r.Path, out var s)) continue;
                bool on = r.Evaluate(s);
                bool was = _active.TryGetValue(r, out var since);
                if (on && !was)
                {
                    _active[r] = ts;
                    s.TryGetValue(r.Property, out var v);
                    events.Add(new WatchEvent(ts, r.Path, r.Property, null, v, WatchEventKind.RuleOn, RuleName(r)));
                }
                else if (!on && was)
                {
                    _active.Remove(r);
                    events.Add(new WatchEvent(ts, r.Path, r.Property, null, null, WatchEventKind.RuleOff, RuleName(r), (ts - since).TotalMilliseconds));
                }
            }
            _log.AddRange(events);
            if (_log.Count > MaxLog) _log.RemoveRange(0, _log.Count - MaxLog);
        }
        return events;
    }

    private static string RuleName(WatchRule r) => r.Name.Length > 0 ? r.Name : r.Describe();

    private long Resolve(long dataModel, string path)
    {
        if (_cache.TryGetValue(path, out var c) && reader.GetName(c.Addr) == c.Name && reader.GetClassName(c.Addr) == c.Cls)
            return c.Addr;
        long inst = reader.ResolvePath(dataModel, path);
        if (inst != 0) _cache[path] = (inst, reader.GetName(inst), reader.GetClassName(inst));
        else _cache.Remove(path);
        return inst;
    }

    private Dictionary<string, string?> Sample(long dataModel, string path)
    {
        var d = new Dictionary<string, string?>(StringComparer.Ordinal);
        long inst = Resolve(dataModel, path);
        d["Exists"] = inst != 0 ? "true" : "false";
        if (inst == 0) return d;
        string cls = reader.GetClassName(inst);
        d["ClassName"] = cls;
        d["ChildCount"] = reader.GetChildren(inst).Count.ToString();
        foreach (var (k, v) in properties.Read(inst, cls)) d[k] = v;
        return d;
    }
}
