# ADR 0002：推理改成进程内的 Sdcb.HyMT2Sharp（弃用 llama.cpp）

- 状态：已采纳
- 日期：2026-11-25

## 背景

MVP 用 llama.cpp 的 `llama-server` 跑 Hy-MT2 1.8B：应用在第一次翻译时拉起子进程，等 `/health`，
请求走 `127.0.0.1:18080` 的 OpenAI 兼容接口，闲置 10 分钟杀进程。为了让「不常驻占内存」成立，
围绕进程生命周期写了一整套东西：端口探测、随机 `--api-key`、健康检查超时、闲置计时器、
请求计数防误杀、退出时杀进程树，以及安装时的运行时下载与模型下载校验。

这套东西的维护成本和故障面都不小：端口被上一次残留的进程占住、关窗后进程不死、
闲置计时被悬停请求无限续命——这些都在日志里真实发生过（见 w3 的复盘）。

## 决定

用 [Sdcb.HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp)（Apache-2.0，纯 C#）在应用进程内推理，
权重换成腾讯官方的 **Hy-MT3 1.8B Q4_K_M**，通过 NuGet 包 `Sdcb.GgufWeights.Hy-MT3-1.8B-Q4_K_M`
（含 Part2–Part6）随应用分发。

随之删除：

- `LlamaServerManager`、`LlamaServerState`、`LlamaServerOptions`、`LlamaTranslationClient`（HTTP/SSE）；
- 设置页的 llama-server 路径、GGUF 路径、端口、闲置退出与「停止模型」；
- `packaging/deploy-model.ps1`、`model-manifest.json`、`scripts/setup-model.*`，以及安装时的模型下载步骤。

新增 `Core/Model/HunyuanEngine`：懒加载权重、串行化请求、用 `Channel` 把逐 token 的文本送回调用方。

## 后果

好的方面：

- 没有子进程、端口、API key 和闲置计时器；关窗即退出这件事不再依赖杀进程树。
- 装完就能翻译：不再有「安装时联网下载 1.1 GB 模型」这一步，也不会因为下载失败得到一个不能用的应用。
- 首译更快：加载 2.3 秒（权重在页缓存时），首译 0.3–0.5 秒；CPU prefill 比 llama.cpp 快（短句 100–170 ms）。
- 权重、运行时、许可都在 NuGet 里，版本可复现，CI 直接 `restore` 就够。

代价（都已实测，不是估计）：

- **内存约 4.4 GB**（llama.cpp 方案约 1.2–1.8 GB）：Q4_K 权重被重排成 8 列 panel，外加常驻 KV 缓存约 250 MB。
- **安装包约 1.3 GB**（原 67 MB）：权重必须随包。GitHub Release 单文件上限 2 GB，再换更大的量化就得改发布方式。
- **发布必须关掉 single-file**：1.3 GB 程序集打进单文件只会让首次启动再解一遍。
- **Vulkan 不总是可用**：部分驱动能枚举设备却在加载权重时失败，引擎必须能回退 CPU 并如实告诉用户。
- 模型常驻到应用退出，没有「闲置释放内存」的开关了——这是刻意的简化，要回来就得重新引入生命周期管理。

## 备选方案

- **继续用 llama.cpp，只换 MT3 权重**：改动最小，但进程/端口/闲置那套复杂度原样保留，MT3 的 NuGet 权重也用不上。
- **进程内推理但保留外部 GGUF 下载**：安装包能维持 67 MB，代价是回到「装完还要下 1.3 GB、失败就不可用」，
  而且 NuGet 权重包的价值消失。
- **换 2Bit / 1.25Bit 量化**：内存和安装包都能小一半以上（573 MB / 440 MB），首译也更快；
  但本项目是翻译工具，精度优先，先按 Q4_K_M 验收，若内存成为普遍问题再考虑降档。
