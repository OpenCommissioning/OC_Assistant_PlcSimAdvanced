using OC.Assistant.Sdk.Plugin;
using Siemens.Simatic.Simulation.Runtime;

namespace OC.PlcSimAdvanced;

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
    
    public SDataRecordInfo Info => _info;
    
    public byte[]? Data { get; }
    
    private ushort Identifier => (ushort)_identifier;
    private ushort HardwareId => (ushort)Info.HardwareId;
    private ushort Index => (ushort)Info.RecordIdx;
    
    public uint CbLength => _info.DataSize;

    public RecordDataTelegram ToRecordDataRequest() => new(Identifier, HardwareId, Index, CbLength, Data);
    
    public string LogMessage
    {
        get
        {
            return _telegram switch
            {
                Telegram.WrRec => $"WrRec  Identifier {Identifier}  HardwareId {HardwareId}  Index {Index}  Data {BitConverter
                    .ToString(Data ?? [], 0, Math.Min(10, Data?.Length ?? 0))}",
                Telegram.RdRec => $"RdRec  Identifier {Identifier}  HardwareId {HardwareId}  Index {Index}",
                Telegram.WrRes => $"WrRes  Identifier {Identifier}  HardwareId {HardwareId}  Index {Index}",
                Telegram.RdRes => $"RdRes  Identifier {Identifier}  HardwareId {HardwareId}  Index {Index}  Data {BitConverter
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
    /// Constructor for RdRes or WrRes telegram, depending on <see cref="e"/>.<see cref="RecordDataTelegram.Data"/>:
    /// <br/>
    /// WrRes: <see cref="RecordDataTelegram.Data"/> is null<br/>
    /// RdRes: <see cref="RecordDataTelegram.Data"/> is not null<br/>
    /// </summary>
    public RecordData(RecordDataTelegram e, ushort identifier)
    {
        _info.HardwareId = e.HardwareId;
        _info.RecordIdx = e.Index;
        _info.DataSize = e.CbLength;
        Data = e.Data;
        _identifier = identifier;
        _telegram = Data is null ? Telegram.WrRes : Telegram.RdRes;
    }
}