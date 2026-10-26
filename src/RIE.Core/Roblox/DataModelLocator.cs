using RIE.Core.Memory;

namespace RIE.Core.Roblox;

public sealed record DataModelRoots(long DataModel, long Workspace, long Players, long LocalPlayer, long PlayerGui, long ReplicatedStorage)
{
    public static readonly DataModelRoots Empty = new(0, 0, 0, 0, 0, 0);
}

/// <summary>DataModel = [moduleBase + FakeDataModel.Pointer] + FakeDataModel.RealDataModel, then the main services.</summary>
public sealed class DataModelLocator(InstanceReader reader)
{
    public long ReadDataModel(long moduleBase)
    {
        var o = reader.Offsets;
        var m = reader.Memory;
        if (moduleBase == 0) return 0;
        if (!o.TryResolve("FakeDataModelPointer", out long fakeOff)) return 0;
        if (!m.TryReadPointer(moduleBase + fakeOff, out long fake)) return 0;
        if (!o.TryResolve("FakeDataModelToDataModel", out long realOff)) return 0;
        return m.TryReadPointer(fake + realOff, out long real) ? real : 0;
    }

    public DataModelRoots Locate(long moduleBase) => LocateFrom(ReadDataModel(moduleBase));

    public DataModelRoots LocateFrom(long dataModel)
    {
        if (!InstanceReader.IsValid(dataModel)) return DataModelRoots.Empty;
        long workspace = reader.FindChildOfClass(dataModel, "Workspace");
        if (workspace == 0 && reader.Offsets.TryResolve("DataModel.Workspace", out long wOff))
            reader.Memory.TryReadPointer(dataModel + wOff, out workspace);
        long players = reader.FindChildOfClass(dataModel, "Players");
        long local = 0;
        if (players != 0 && reader.Offsets.TryResolve("LocalPlayer", out long lpOff))
            reader.Memory.TryReadPointer(players + lpOff, out local);
        long playerGui = local != 0 ? reader.FindChildOfClass(local, "PlayerGui") : 0;
        long rs = reader.FindChildOfClass(dataModel, "ReplicatedStorage");
        return new DataModelRoots(dataModel, workspace, players, local, playerGui, rs);
    }
}
