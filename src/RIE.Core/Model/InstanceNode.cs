namespace RIE.Core.Model;

/// <summary>Ringkasan satu instance yang sudah dibaca.</summary>
public sealed record InstanceInfo(long Address, string Name, string ClassName, string Path);
