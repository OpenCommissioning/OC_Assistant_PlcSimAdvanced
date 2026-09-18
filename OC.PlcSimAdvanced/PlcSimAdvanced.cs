using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Siemens.Simatic.Simulation.Runtime;
using OC.Assistant.Sdk;
using OC.Assistant.Sdk.Plugin;

namespace OC.PlcSimAdvanced;

[PluginDelayAfterStart(2000)]
public class PlcSimAdvanced : PluginBase
{
    [PluginParameter("Name of the PlcSim Advanced instance")]
    private readonly string _plcName = "PLC_1";
        
    [PluginParameter("Unique id for acyclic communication")]
    private readonly ushort _identifier = 1;
        
    [PluginParameter("CycleTime in ms")]
    private readonly int _cycleTime = 10;
    
    [PluginParameter("e.g. 0-1023 or 0,1,2 or a combination")]
    private readonly string _inputAddress = "0-1023";
        
    [PluginParameter("e.g. 0-1023 or 0,1,2 or a combination")]
    private readonly string _outputAddress = "0-1023";
    
    [PluginParameter("One or multiple Tags to read, defined as TagName:TagType, separated by semicolon")]
    private readonly string _tagsToRead = "";
        
    [PluginParameter("One or multiple Tags to write, defined as TagName:TagType, separated by semicolon")]
    private readonly string _tagsToWrite = "";
        
    private IInstance? _instance;
    private readonly StopwatchEx _stopwatch = new ();
    private byte[] _inputArea = [];
    private byte[] _outputArea = [];
    private double _timeScaling = 1.0;
    private int[] _inputList = [];
    private int[] _outputList = [];
    private Tag[] _readTags = [];
    private Tag[] _writeTags = [];
    private SDataValueByName[] _readSignals = [];
    private SDataValueByName[] _writeSignals = [];

    /// <param name="Name">Name of the tag in the PlcSim Advanced instance.</param>
    /// <param name="Type">Type of the tag.</param>
    /// <param name="BitOffset">Offset of the tag in the <see cref="InputBuffer"/> or <see cref="OutputBuffer"/>.</param>
    private readonly record struct Tag(string Name, TcType Type, int BitOffset);

    public PlcSimAdvanced()
    {
        EventSystem.ApiDataReceived += OnApiDataReceived;
    }

    private void OnApiDataReceived(string identifier, XElement payload)
    {
        if (_instance is null) return;
        if (identifier != "data/timeScaling" || !double.TryParse(payload.Value, out var timeScaling)) return;
        
        if (!(Math.Abs(_timeScaling - timeScaling) > 0.001)) return;
        Logger.LogInfo(this, $"ScaleFactor for Plc '{_plcName}' changed from {_timeScaling} to {timeScaling}");
        _timeScaling = timeScaling;
        _instance.ScaleFactor = timeScaling;
    }

    protected override bool OnSave()
    {
        _inputList = _inputAddress.ToNumberList();
        _outputList = _outputAddress.ToNumberList();

        try
        {
            _writeTags = ParseTags(_tagsToWrite, _inputList.Length);
            _readTags = ParseTags(_tagsToRead, _outputList.Length);
        }
        catch (FormatException e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }

        foreach (var address in _inputList)
        {
            InputStructure.AddVariable($"I{address}", TcType.Byte);
        }

        foreach (var address in _outputList)
        {
            OutputStructure.AddVariable($"Q{address}", TcType.Byte);
        }
        
        foreach (var tag in _writeTags)
        {
            InputStructure.AddVariable(ToVariableName(tag.Name), tag.Type);
        }

        foreach (var tag in _readTags)
        {
            OutputStructure.AddVariable(ToVariableName(tag.Name), tag.Type);
        }
        
        return true;
    }

    protected override bool OnStart()
    {
        _stopwatch.Restart();
        
        if (!WaitForConnection(CancellationToken))
        {
            Logger.LogWarning(this, $"Connecting to Plc '{_plcName}' aborted");
            return false;
        }

        if (_instance is null) return false;
        _inputArea = new byte[_instance.InputArea.AreaSize];
        _outputArea = new byte[_instance.OutputArea.AreaSize];
        _timeScaling = _instance.ScaleFactor;
        CreateSignalList(_instance, ref _writeTags, out _writeSignals);
        CreateSignalList(_instance, ref _readTags, out _readSignals);

        Logger.LogInfo(this, $"Connected to Plc '{_plcName}'");
        return true;
    }

    protected override void OnUpdate()
    {
        if (_instance is null) return;
        _stopwatch.WaitUntil(_cycleTime);

        try
        {
            if (_instance.OperatingState != EOperatingState.Run)
            {
                //Wait a sec...
                Thread.Sleep(1000);
                    
                //Complete shutdown -> disconnect
                if (_instance.OperatingState == EOperatingState.InvalidOperatingState)
                {   
                    var size = OutputBuffer.Length;
                    Array.Copy(new byte[size], OutputBuffer, size);
                    CancellationRequest();
                    return;
                }
            }

            //Ads read
            for (var i = 0; i < _inputList.Length; ++i) _inputArea[_inputList[i]] = InputBuffer[i];

            //Write Plc inputs
            _instance.InputArea.WriteBytes(0, (uint)_inputArea.Length, _inputArea);

            //Write Plc tags
            if (_writeSignals.Length > 0)
            {
                var inputBuffer = InputBuffer;
                for (var i = 0; i < _writeSignals.Length; ++i)
                {
                    _writeSignals[i].DataValue = ToDataValue(_writeTags[i], inputBuffer);
                }
                _instance.WriteSignals(ref _writeSignals);
            }

            //Read Plc tags
            if (_readSignals.Length > 0)
            {
                _instance.ReadSignals(ref _readSignals);
                var outputBuffer = OutputBuffer;
                for (var i = 0; i < _readSignals.Length; ++i)
                {
                    ToBuffer(_readSignals[i].DataValue, _readTags[i], outputBuffer);
                }
            }

            //Read Plc outputs
            _outputArea = _instance.OutputArea.ReadBytes(0, (uint)_outputArea.Length);

            //Ads write
            for (var i = 0; i < _outputList.Length; ++i) OutputBuffer[i] = _outputArea[_outputList[i]];

            var elapsedMilliseconds = _stopwatch.ElapsedMilliseconds;
            if ((int)elapsedMilliseconds > _cycleTime * 2)
            {
                Logger.LogWarning(this, $"CycleTime of Plc '{_plcName}' exceeded. DeltaTime = {(int)elapsedMilliseconds} ms");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(this, ex.Message, true);
            Thread.Sleep(200);
        }
    }

    protected override void OnStop()
    {
        try
        {
            if (_instance is not null)
            {
                _instance.OnDataRecordRead -= OnDataRecordRead;
                _instance.OnDataRecordWrite -= OnDataRecordWrite;
                _instance.OnOperatingStateChanged -= InstanceOnOperatingStateChanged;
                _instance.Dispose();
                _instance = null;
            }
            
            RecordDataServer.OnWriteRes -= OnWriteRes;
            RecordDataServer.OnReadRes -= OnReadRes;
            _readSignals = [];
            _writeSignals = [];
        }
        catch (Exception ex)
        {
            Logger.LogError(this, ex.Message, true);
        }
            
        Logger.LogInfo(this, $"Disconnected from Plc '{_plcName}'");
    }
        
    private bool WaitForConnection(CancellationToken token)
    {
        if (!SimulationRuntimeManager.IsRuntimeManagerAvailable)
        {
            Logger.LogError(this,"PlcSim Advanced not available");
            return false;
        }
            
        Logger.LogInfo(this, $"Trying to start and connect Plc '{_plcName}'...");

        try
        {
            _instance = SimulationRuntimeManager.CreateInterface(_plcName);
            _instance.PowerOn();

            while (_instance.OperatingState != EOperatingState.Run && !token.IsCancellationRequested)
            {
                if (_instance.OperatingState == EOperatingState.ShuttingDown)
                {
                    return false;
                }

                if (_stopwatch.ElapsedMilliseconds > 5000) return false;
                Thread.Sleep(100);
            }

            if (_instance.OperatingState != EOperatingState.Run)
            {
                return false;
            }
            
            RecordDataServer.OnWriteRes += OnWriteRes;
            RecordDataServer.OnReadRes += OnReadRes;

            _instance.UpdateTagList();
            _instance.OnDataRecordRead += OnDataRecordRead;
            _instance.OnDataRecordWrite += OnDataRecordWrite;
            _instance.OnOperatingStateChanged += InstanceOnOperatingStateChanged;
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }
    }

    /// <summary>
    /// Parses tag definitions (e.g. <c>Tag1:Int;"DB1".Tag2:Real</c>) and calculates the offsets
    /// the same way the <see cref="PluginBase.InputStructure"/> and <see cref="PluginBase.OutputStructure"/> do.
    /// </summary>
    /// <param name="definitions">Tag definitions as TagName:TagType, separated by semicolon.</param>
    /// <param name="byteOffset">Byte offset of the first tag.</param>
    private static Tag[] ParseTags(string definitions, int byteOffset)
    {
        var tags = new List<Tag>();
        var bitOffset = byteOffset * 8;

        foreach (var definition in definitions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = definition.LastIndexOf(':');
            var name = separator > 0 ? definition[..separator].Trim() : "";
            var typeName = separator > 0 ? definition[(separator + 1)..].Trim() : "";

            if (name == "" || !Enum.TryParse<TcType>(typeName, true, out var type) ||
                !Enum.IsDefined(type) || type == TcType.Unknown)
            {
                throw new FormatException($"Invalid tag definition '{definition}'. Expected TagName:TagType");
            }

            //Every type except Bit starts at a full byte
            if (type != TcType.Bit && bitOffset % 8 != 0) bitOffset += 8 - bitOffset % 8;
            tags.Add(new Tag(name, type, bitOffset));
            bitOffset += type.BitSize();
        }

        return [.. tags];
    }

    /// <summary>
    /// Converts a Plc tag name (e.g. <c>"DB1".Tag</c>) into a valid variable name (e.g. <c>DB1_Tag</c>).
    /// </summary>
    private static string ToVariableName(string tagName)
        => Regex.Replace(tagName, "[^a-zA-Z0-9_]+", "_").Trim('_');

    /// <summary>
    /// Creates a signal list for the given tags and removes tags that the Plc rejects.<br/>
    /// The list is reused for every following access, because it caches internal information.
    /// </summary>
    private void CreateSignalList(IInstance instance, ref Tag[] tags, out SDataValueByName[] signals)
    {
        var signalList = ToSignalList(tags);
        
        if (signalList.Length > 0)
        {
            //A single read checks names and types, the result is reported per signal
            try
            {
                instance.ReadSignals(ref signalList);
            }
            catch (SimulationRuntimeException e) when (e.RuntimeErrorCode == ERuntimeErrorCode.SignalConfigurationError)
            {
                //At least one signal error is in the list, see the error code of the single signals below
            }

            var validTags = new List<Tag>();
            for (var i = 0; i < signalList.Length; i++)
            {
                if (signalList[i].ErrorCode == ERuntimeErrorCode.OK)
                {
                    validTags.Add(tags[i]);
                    continue;
                }
                Logger.LogWarning(this, $"Tag '{tags[i].Name}' of type {tags[i].Type.Name()} is ignored: {signalList[i].ErrorCode}");
            }

            //The set of signals has changed, so a new list is needed
            if (validTags.Count != tags.Length)
            {
                tags = validTags.ToArray();
                signalList = ToSignalList(tags);
            }
        }
        
        signals = signalList;
    }

    /// <summary>
    /// Creates a signal list with the name and the expected type of every tag.
    /// </summary>
    private static SDataValueByName[] ToSignalList(Tag[] tags)
    {
        return tags.Select(tag => new SDataValueByName
        {
            Name = tag.Name,
            DataValue = new SDataValue { Type = ToPrimitiveDataType(tag.Type) }
        }).ToArray();
    }

    /// <summary>
    /// Returns the <see cref="EPrimitiveDataType"/> of a <see cref="TcType"/>.
    /// </summary>
    private static EPrimitiveDataType ToPrimitiveDataType(TcType type) => type switch
    {
        TcType.Bit or TcType.Bool => EPrimitiveDataType.Bool,
        TcType.Byte or TcType.UsInt => EPrimitiveDataType.UInt8,
        TcType.SInt => EPrimitiveDataType.Int8,
        TcType.Word or TcType.Uint => EPrimitiveDataType.UInt16,
        TcType.Int => EPrimitiveDataType.Int16,
        TcType.Dword or TcType.UDint => EPrimitiveDataType.UInt32,
        TcType.Dint => EPrimitiveDataType.Int32,
        TcType.Real => EPrimitiveDataType.Float,
        TcType.LWord or TcType.ULint => EPrimitiveDataType.UInt64,
        TcType.Lint => EPrimitiveDataType.Int64,
        TcType.LReal => EPrimitiveDataType.Double,
        _ => EPrimitiveDataType.Unspecific
    };

    /// <summary>
    /// Reads the value of a tag from the buffer. Setting the value also sets the type.<br/>
    /// Multibyte values are stored in little endian, matching the type of the structure variable.
    /// </summary>
    private static SDataValue ToDataValue(Tag tag, byte[] buffer)
    {
        var offset = tag.BitOffset / 8;
        var span = buffer.AsSpan(offset);
        var value = new SDataValue();

        switch (tag.Type)
        {
            case TcType.Bit:
                value.Bool = (buffer[offset] & 1 << tag.BitOffset % 8) != 0;
                break;
            case TcType.Bool:
                value.Bool = span[0] != 0;
                break;
            case TcType.Byte or TcType.UsInt:
                value.UInt8 = span[0];
                break;
            case TcType.SInt:
                value.Int8 = (sbyte)span[0];
                break;
            case TcType.Word or TcType.Uint:
                value.UInt16 = BinaryPrimitives.ReadUInt16LittleEndian(span);
                break;
            case TcType.Int:
                value.Int16 = BinaryPrimitives.ReadInt16LittleEndian(span);
                break;
            case TcType.Dword or TcType.UDint:
                value.UInt32 = BinaryPrimitives.ReadUInt32LittleEndian(span);
                break;
            case TcType.Dint:
                value.Int32 = BinaryPrimitives.ReadInt32LittleEndian(span);
                break;
            case TcType.Real:
                value.Float = BinaryPrimitives.ReadSingleLittleEndian(span);
                break;
            case TcType.LWord or TcType.ULint:
                value.UInt64 = BinaryPrimitives.ReadUInt64LittleEndian(span);
                break;
            case TcType.Lint:
                value.Int64 = BinaryPrimitives.ReadInt64LittleEndian(span);
                break;
            case TcType.LReal:
                value.Double = BinaryPrimitives.ReadDoubleLittleEndian(span);
                break;
        }

        return value;
    }

    /// <summary>
    /// Writes the value of a tag to the buffer.<br/>
    /// Multibyte values are stored in little endian, matching the type of the structure variable.
    /// </summary>
    private static void ToBuffer(SDataValue value, Tag tag, byte[] buffer)
    {
        var offset = tag.BitOffset / 8;
        var span = buffer.AsSpan(offset);

        switch (tag.Type)
        {
            case TcType.Bit:
                var mask = (byte)(1 << tag.BitOffset % 8);
                if (value.Bool) buffer[offset] |= mask;
                else buffer[offset] &= (byte)~mask;
                break;
            case TcType.Bool:
                span[0] = value.Bool ? (byte)1 : (byte)0;
                break;
            case TcType.Byte or TcType.UsInt:
                span[0] = value.UInt8;
                break;
            case TcType.SInt:
                span[0] = (byte)value.Int8;
                break;
            case TcType.Word or TcType.Uint:
                BinaryPrimitives.WriteUInt16LittleEndian(span, value.UInt16);
                break;
            case TcType.Int:
                BinaryPrimitives.WriteInt16LittleEndian(span, value.Int16);
                break;
            case TcType.Dword or TcType.UDint:
                BinaryPrimitives.WriteUInt32LittleEndian(span, value.UInt32);
                break;
            case TcType.Dint:
                BinaryPrimitives.WriteInt32LittleEndian(span, value.Int32);
                break;
            case TcType.Real:
                BinaryPrimitives.WriteSingleLittleEndian(span, value.Float);
                break;
            case TcType.LWord or TcType.ULint:
                BinaryPrimitives.WriteUInt64LittleEndian(span, value.UInt64);
                break;
            case TcType.Lint:
                BinaryPrimitives.WriteInt64LittleEndian(span, value.Int64);
                break;
            case TcType.LReal:
                BinaryPrimitives.WriteDoubleLittleEndian(span, value.Double);
                break;
        }
    }

    private void InstanceOnOperatingStateChanged(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, EOperatingState prevState, EOperatingState operatingState)
    {
        Logger.LogInfo(this, $"Plc '{sender.Name}' changed from state '{prevState}' to '{operatingState}'");
    }

    private void OnDataRecordWrite(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecord e)
    {
        Logger.LogInfo(this, $"WrRec  Identifier {_identifier}  HardwareId {e.Info.HardwareId}  Index {e.Info.RecordIdx}  Data {BitConverter
            .ToString(e.Data, 0, Math.Min(10, e.Data.Length))}", true);
        RecordDataServer.WriteReq(e.Info.ToRecordDataTelegram(_identifier, e.Data));
    }

    private void OnDataRecordRead(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecordInfo e)
    {
        Logger.LogInfo(this, $"RdRec  Identifier {_identifier}  HardwareId {e.HardwareId}  Index {e.RecordIdx}", true);
        RecordDataServer.ReadReq(e.ToRecordDataTelegram(_identifier));
    }
        
    private void OnWriteRes(RecordDataTelegram e)
    {
        if (e.Identifier != _identifier) return;
        var d = e.ToSDataRecord();
        Logger.LogInfo(this, $"WrRes  Identifier {_identifier}  HardwareId {d.Info.HardwareId}  Index {d.Info.RecordIdx}", true);
        _instance?.WriteRecordDone(d.Info, 0);
    }

    private void OnReadRes(RecordDataTelegram e)
    {
        if (e.Identifier != _identifier) return;
        var d = e.ToSDataRecord();
        Logger.LogInfo(this, $"RdRes  Identifier {_identifier}  HardwareId {d.Info.HardwareId}  Index {d.Info.HardwareId}  Data {BitConverter
            .ToString(d.Data, 0, Math.Min(10, d.Data.Length))}", true);
        _instance?.ReadRecordDone(d.Info, d.Data, 0);
    }
}