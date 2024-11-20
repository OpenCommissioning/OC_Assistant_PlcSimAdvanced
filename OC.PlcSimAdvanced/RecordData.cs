using Siemens.Simatic.Simulation.Runtime;

namespace OC.PlcSimAdvanced;

/// <summary>
/// Class representing an acyclic telegram.
/// </summary>
public class RecordData
{
    private enum Telegram
    {
        WrRec,
        RdRec,
        WrRes,
        RdRes
    }

    private readonly SDataRecordInfo _info;
    private readonly int _identifier;
    private readonly Telegram _telegram;

    /// <summary>
    /// Siemens related information about the telegram.
    /// </summary>
    public SDataRecordInfo Info => _info;
        
    /// <summary>
    /// Data of the telegram. Can be null.
    /// </summary>
    public byte[]? Data { get; }

    /// <summary>
    /// InvokeId of the telegram.
    /// </summary>
    public uint InvokeId => Info.RecordIdx * 0x10000 + Info.HardwareId;
        
    /// <summary>
    /// IndexGroup of the telegram.
    /// </summary>
    public uint IndexGroup => 0x80000000 + Info.RecordIdx;
        
    /// <summary>
    /// IndexOffset of the telegram.
    /// </summary>
    public uint IndexOffset => Info.HardwareId + (uint)_identifier * 0x10000;

    /// <summary>
    /// Get information about the telegram.
    /// </summary>
    public string Message
    {
        get
        {
            return _telegram switch
            {
                Telegram.WrRec => $"WrRec  IGrp {IndexGroup:X}  IOffs {IndexOffset:X}  Data {BitConverter
                    .ToString(Data ?? [], 0, Math.Min(10, Data?.Length ?? 0))}",
                Telegram.RdRec => $"RdRec  IGrp {IndexGroup:X}  IOffs {IndexOffset:X}",
                Telegram.WrRes => $"WrRes  IGrp {IndexGroup:X}  IOffs {IndexOffset:X}",
                Telegram.RdRes => $"RdRes  IGrp {IndexGroup:X}  IOffs {IndexOffset:X}  Data {BitConverter
                    .ToString(Data ?? [], 0, Math.Min(10, Data?.Length ?? 0))}",
                _ => ""
            };
        }
    }

    /// <summary>
    /// Constructor for RdRec telegram.
    /// </summary>
    public RecordData(SDataRecordInfo sDataRecordInfo, int identifier)
    {
        _info = sDataRecordInfo;
        _identifier = identifier;
        _telegram = Telegram.RdRec;
    }

    /// <summary>
    /// Constructor for WrRec telegram.
    /// </summary>
    public RecordData(SDataRecord sDataRecord, int identifier)
    {
        _info = sDataRecord.Info;
        Data = sDataRecord.Data;
        _identifier = identifier;
        _telegram = Telegram.WrRec;
    }

    /// <summary>
    /// Constructor for RdRes or WrRes telegram, depending on <see cref="e"/>.<see cref="AdsServer.Response.Data"/>:
    /// <br/>
    /// WrRes: <see cref="AdsServer.Response.Data"/> is null<br/>
    /// RdRes: <see cref="AdsServer.Response.Data"/> is not null<br/>
    /// </summary>
    public RecordData(AdsServer.Response e, int identifier)
    {
        _info.HardwareId = (ushort)e.InvokeId;
        _info.RecordIdx = e.InvokeId >> 16;
        _info.DataSize = e.CbLength;
        Data = e.Data;
        _identifier = identifier;
        _telegram = Data is null ? Telegram.WrRes : Telegram.RdRes;
    }
}