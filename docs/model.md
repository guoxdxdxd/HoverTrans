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

沿用 Hy-MT2 的指令格式（英文、语言名用全称），不添加系统提示词：一句指令 + 空行 + 原文。翻译页语言对右侧的「意译」勾选切换两种指令，同一个模型、同一套启动参数。

**直译（默认，`TranslationStyle.Literal`）**

```text
Translate the following text into Simplified Chinese. Note that you should only output the translated result without any additional explanation.

<source text>
```

**意译（勾选「意译」，`TranslationStyle.Natural`）**

Hy-MT2 支持用方括号里的风格子句控制语域，意译就是在这条指令上加一句风格：

```text
Translate the following text into Simplified Chinese. Note that the translation style must strictly conform to [colloquial, natural everyday speech: keep the speaker's tone and force, and render slang, idioms and wordplay with their established equivalents instead of word-for-word literals], and that you should only output the translated result without any additional explanation.

<source text>
```

- 手动指定源语言时写成 `Translate the following English text into ...`；目标语言用英文全称（`Simplified Chinese`、`Traditional Chinese`、`Japanese` 等），由 `LanguageCatalog` 映射。
- 勾选状态存在 `settings.json` 的 `FreeTranslation`，重启后保留；悬停浮窗固定直译，避免取词时为了"顺"而加戏。
- 意译更容易多出解释或把意思放大：验收看三条——意思准、没有额外解释、长度没有异常膨胀。

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
