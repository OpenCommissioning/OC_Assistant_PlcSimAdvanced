namespace OC.PlcSimAdvanced;

public interface IRecordDataHandle
{
    /// <summary>
    /// Adds a new write request to the FIFO collection.
    /// </summary>
    public void AddWriteReq(RecordData recordData);

    /// <summary>
    /// Adds a new read request to the FIFO collection.
    /// </summary>
    public void AddReadReq(RecordData recordData);

    /// <summary>
    /// Receives write responses.
    /// </summary>
    public event Action<AdsServer.Response>? OnWriteRes;
        
    /// <summary>
    /// Receives read responses.
    /// </summary>
    public event Action<AdsServer.Response>? OnReadRes;
}