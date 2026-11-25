namespace HoverTrans.Core.Model;

/// <summary>How the local model is doing. In-process, so there is no process state to wait on.</summary>
public enum EngineState
{
    /// <summary>Weight files are not read yet; the first translation loads them.</summary>
    NotLoaded,

    /// <summary>Reading the GGUF weights and preparing the KV cache.</summary>
    Loading,

    /// <summary>Loaded and ready to translate.</summary>
    Ready,

    /// <summary>Loading failed; <see cref="HunyuanEngine.LastError"/> says why.</summary>
    Failed
}
