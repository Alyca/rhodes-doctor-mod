# 不稳定血浆：旧规范作废（Full Reset）

> 本文件原先的 `LineRenderer` 线条方案已被作者全盘否决，旧内容已删除。
> 不要按旧方案实现，也不要对 `src/UnstablePlasmaFx.cs` 现有代码做增量修补。

`src/UnstablePlasmaFx.cs` 的视觉设计、几何、材质、动画数学与代码结构，**全权交由 Macroscope 自主重写**。Macroscope 应直接给出完整替换文件，而不是针对旧实现的局部 suggestion。

## 不可协商的边界（仅此而已）
- 不要线条 / 线框 / 细带式主视觉（不以 `LineRenderer` / `TrailRenderer` 作为画面主体）。
- 自绘：不借原版粒子 prefab，不用 `Shader.Find`；沿用项目已验证的网格/面片 + 克隆材质路径。
- 保留 `UnstablePlasmaFx.Tracker`、`UnstablePlasmaAnim : MarkFxBase` 的接入契约，零运行时 GC，`Master` 0.3s 淡出，完整销毁。

除此之外，视觉语言与实现方式由 Macroscope 全权决定。
