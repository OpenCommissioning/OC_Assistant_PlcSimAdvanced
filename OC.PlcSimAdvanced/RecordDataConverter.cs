using OC.Assistant.Sdk.Plugin;
using Siemens.Simatic.Simulation.Runtime;

namespace OC.PlcSimAdvanced;

public static class RecordDataConverter
{
    public static RecordDataTelegram ToRecordDataTelegram(this SDataRecordInfo info, ushort identifier, byte[]? data = null)
        => new (identifier, (ushort)info.HardwareId, (ushort)info.RecordIdx, info.DataSize, data);
    
    public static SDataRecord ToSDataRecord(this RecordDataTelegram telegram)
        => new ()
        {
            Info = new SDataRecordInfo
            {
                HardwareId = telegram.HardwareId,
                RecordIdx = telegram.Index,
                DataSize = telegram.CbLength
            },
            Data = telegram.Data ?? []
        };
}