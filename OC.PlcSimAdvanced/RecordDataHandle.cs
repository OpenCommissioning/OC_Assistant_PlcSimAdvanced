using System.Collections.Concurrent;
using OC.Assistant.Sdk;

namespace OC.PlcSimAdvanced;

/// <summary>
/// Singleton class for the acyclic data (record data) handling between PlcSimAdvanced and TwinCAT.
/// Uses FIFO logic for read and write requests.
/// Raises read and write response events.
/// </summary>
internal class RecordDataHandle : IRecordDataHandle
{
    private static readonly Lazy<RecordDataHandle> LazyInstance = new(() => new RecordDataHandle());
    private readonly ConcurrentQueue<RecordData> _writeReq = new();
    private readonly ConcurrentQueue<RecordData> _readReq = new();
    private readonly AdsServer? _adsServer;
        
    /// <summary>
    /// Constructor initializes a new TcAdsServer and starts the cyclic task.
    /// </summary>
    private RecordDataHandle()
    {
        if (LazyInstance.IsValueCreated) return;
        
        _adsServer = new AdsServer();
        _adsServer.OnAdsReadCon += AdsServerOnAdsReadCon;
        _adsServer.OnAdsWriteCon += AdsServerOnAdsWriteCon;
        _adsServer.ConnectServer();
        Task.Run(MainCycle);
    }
        
    /// <summary>
    /// Interface for the acyclic data (record data) handling between PlcSimAdvanced and TwinCAT.
    /// </summary>
    public static IRecordDataHandle Instance => LazyInstance.Value;

    public void AddWriteReq(RecordData recordData)
    {
        _writeReq.Enqueue(recordData);
    }

    public void AddReadReq(RecordData recordData)
    {
        _readReq.Enqueue(recordData);
    }
    
    public event Action<AdsServer.Response>? OnWriteRes;
    
    public event Action<AdsServer.Response>? OnReadRes;

    /// <summary>
    /// Raises the write response event when receiving a AdsWriteCon.
    /// </summary>
    private void AdsServerOnAdsWriteCon(AdsServer.Response e)
    {
        OnWriteRes?.Invoke(e);
    }

    /// <summary>
    /// Raises the read response event when receiving a AdsReadCon.
    /// </summary>
    private void AdsServerOnAdsReadCon(AdsServer.Response e)
    {
        OnReadRes?.Invoke(e);
    }
        
    /// <summary>
    /// Cyclic task to send read and write requests to the ADS system.
    /// </summary>
    private void MainCycle()
    {
        var stopwatch = new StopwatchEx();
                
        while (LazyInstance.IsValueCreated)
        {
            stopwatch.WaitUntil(10);
                
            try
            {
                if (_writeReq.TryDequeue(out var recordData))
                {
                    _adsServer?.AdsWriteReq(recordData.InvokeId, recordData.IndexGroup, recordData.IndexOffset, recordData.Info.DataSize, recordData.Data ?? []);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(this, e.Message, true);
            }
                
            try
            {
                if (_readReq.TryDequeue(out var recordData))
                {
                    _adsServer?.AdsReadReq(recordData.InvokeId, recordData.IndexGroup, recordData.IndexOffset, recordData.Info.DataSize);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(this, e.Message, true);
            }
        }

        if (_adsServer is null) return;
        _adsServer.OnAdsWriteCon -= AdsServerOnAdsWriteCon;
        _adsServer.OnAdsReadCon -= AdsServerOnAdsReadCon;
        _adsServer.Disconnect();
        _adsServer.Dispose();
    }
}