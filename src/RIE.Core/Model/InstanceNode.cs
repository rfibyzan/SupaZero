namespace RIE.Core.Model;

/// <summary>Summary of one instance that has been read.</summary>
public sealed record InstanceInfo(long Address, string Name, string ClassName, string Path);
