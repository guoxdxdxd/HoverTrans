# 模型与推理参数

## 模型

- **腾讯 Hy-MT3 1.8B**，GGUF `Q4_K_M` 量化，约 1.35 GB（6 个分片）。
- `hunyuan-dense` 架构，翻译专用；Q4_K_M 与 Q8_0 都有 NuGet 权重包，本项目用 Q4_K_M。
- 官方仓库：<https://huggingface.co/tencent/Hy-MT3-1.8B-GGUF>

权重不提交到 git，而是通过 NuGet 包 `Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M`（连同 `.Part2`–`.Part6`）还原，
发布时随应用一起出包。加载方式是 `new HunyuanDenseModel(Sdcb.GgufWeights.Hy_MT3_1_8B_Q4_K_M.Weights.Model(), ...)`：
6 个程序集被拼成一个可 seek 的流，模型库不需要文件路径。

## 推理

`Sdcb.HyMT2Sharp`（Apache-2.0，纯 C#，不依赖 llama.cpp / ONNX Runtime）在本进程内跑：

| 项 | 值 |
| --- | --- |
| 后端 | `auto`（macOS 先 Metal，其次 Vulkan，都不可用则 CPU）/ `cpu` / `vulkan` / `metal` |
| CPU 内核 | AVX-512 → AVX-VNNI → AVX2 → `Vector<T>`；ARM64 走 NEON / SDOT |
| 线程 | 0 = 由库按 CPU 拓扑绑物理 P 核（8P+8E 的机器取 8） |
| KV cache | `KvCacheConfig.Memory`：常驻约 250 MB，请求之间复用分配 |
| 上下文 | 权重声明 262144，库把 KV 容量限制在 min(上下文, 4096)；本项目按 4096 token 封顶 |
| 采样 | temperature 0.1、top-p 0.6、top-k 0、无重复惩罚 |
| 单次生成长度 | 2048 token 上限（按提示词长度再收缩） |

生成方式与 llama-server 时期的参数一致，译文口径没有变；不同之处是没有任何对外接口——
`HunyuanEngine.StreamAsync` 直接产出「到目前为止的译文」，SSE 解析、端口、API key 那套都不再存在。

## 提示词

沿用混元翻译模型的指令格式（英文、语言名用全称），不添加系统提示词：一句指令 + 空行 + 原文。
翻译页语言对右侧的「意译」勾选切换两种指令。

**直译（默认，`TranslationStyle.Literal`）**

```text
Translate the following text into Simplified Chinese. Note that you should only output the translated result without any additional explanation.

<source text>
```

**意译（勾选「意译」，`TranslationStyle.Natural`）**

```text
Translate the following text into Simplified Chinese. Note that the translation style must strictly conform to [colloquial, natural everyday speech: keep the speaker's tone and force, and render slang, idioms and wordplay with their established equivalents instead of word-for-word literals], and that you should only output the translated result without any additional explanation.

<source text>
```

- 手动指定源语言时写成 `Translate the following English text into ...`；目标语言用英文全称（`Simplified Chinese`、`Traditional Chinese`、`Japanese` 等），由 `LanguageCatalog` 映射。
- 勾选状态存在 `settings.json` 的 `FreeTranslation`，重启后保留；悬停浮窗固定直译，避免取词时为了"顺"而加戏。
- 意译更容易多出解释或把意思放大：验收看三条——意思准、没有额外解释、长度没有异常膨胀。

## 性能参考（开发机实测）

Ryzen 平台、8 线程、CPU 后端，Q4_K_M：

| 场景 | 实测 |
| --- | --- |
| 首次加载（权重已在页缓存） | 2.3 秒 |
| 加载后工作集 | 约 4.4 GB |
| 首译延迟（短句） | 0.3–0.5 秒（prefill 100–170 ms + 解码） |
| 解码速度 | 13–22 token/s |
| 闲置 | 不退出：权重常驻到应用关闭 |

冷启动（权重不在页缓存、机械盘）加载会更久；实际速度取决于 CPU/GPU 与文本长度。
Vulkan 后端在部分驱动上会在加载权重时失败（`no suitable memory type`），引擎会自动回退 CPU 并把原因写进
`%LOCALAPPDATA%\HoverTrans\logs\engine.log`。

内存比 llama.cpp 方案（约 1.2–1.8 GB）高不少，原因是 Q4_K 权重会被重排成 8 列 panel 以提高吞吐。
这是选型时接受的代价，取舍见 [adr/0002-in-process-inference.md](adr/0002-in-process-inference.md)。

## 许可

- 模型权重：Apache-2.0（随 NuGet 权重包分发）。
- Sdcb.HyMT2Sharp：Apache-2.0。
- 应用本体：MIT。

第三方清单见 [../packaging/THIRD-PARTY-NOTICES.txt](../packaging/THIRD-PARTY-NOTICES.txt)。
