# 模型与推理参数

## 模型

- **腾讯 Hy-MT2 1.8B**，GGUF `Q4_K_M` 量化，约 1.1 GB。
- 33 种语言，翻译专用 dense 模型，Apache-2.0。
- 官方仓库：
  - ModelScope：<https://modelscope.cn/models/Tencent-Hunyuan/Hy-MT2-1.8B-GGUF>
  - Hugging Face：<https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF>

`scripts/setup-model.ps1` 默认从 ModelScope 下载（国内可达），失败再试 Hugging Face。模型权重不提交到 git。

## 推理

`llama-server` 以 OpenAI 兼容接口运行：

```text
POST http://127.0.0.1:18080/v1/chat/completions
{
  "model": "hy-mt2",
  "messages": [{ "role": "user", "content": "<prompt>" }],
  "stream": true,
  "temperature": 0.1,
  "top_p": 0.6,
  "max_tokens": 2048
}
```

启动参数：

```text
--model <gguf> --host 127.0.0.1 --port 18080 --ctx-size 4096 --parallel 1 --n-gpu-layers 99
```

- `--n-gpu-layers 99`：有 GPU 时尽量全部卸载；纯 CPU 机器 llama.cpp 会忽略不可用后端。
- `--parallel 1`：桌面单用户，避免并发拖慢首译。
- 上下文 4096 足够单次长文分段；超长文本由 `TextChunker` 切分。

## 提示词

沿用 Hy-MT 系列的直译指令，不添加系统提示词：

```text
Translate the following text into Simplified Chinese, without additional explanation.

<source text>
```

手动指定源语言时会写成 `Translate the following English text into ...`。目标语言用英文名（`Simplified Chinese`、`Traditional Chinese`、`Japanese` 等），由 `LanguageCatalog` 映射。

## 性能参考

| 场景 | 预期 |
| --- | --- |
| 首次加载（NVMe，CPU） | 5–20 秒 |
| 已加载，短句翻译 | 0.2–1 秒 |
| 运行时内存 | 1.2–1.8 GB |
| 闲置退出 | 10 分钟（可设置 1–120） |

实际速度取决于 CPU/GPU、模型是否驻留页缓存、文本长度。若显卡显存不足，把 `GpuLayers` 调低或改用 CPU 构建。

## 许可

模型权重为 Apache-2.0；llama.cpp 为 MIT。分发应用时请保留第三方许可说明，并让用户自行下载模型。
