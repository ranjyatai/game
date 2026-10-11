# Sky Prison —— 协作须知

Unity URP 2.5D 抽取式射击游戏。主角 AXIA。全程用中文交流。

## 渲染架构中已经踩过的坑

**相机栈的后处理只能开在栈尾相机上。**
栈是 `Main Camera(Base) → GamePlayCamera(Overlay) → OverheadUICamera(Overlay)`。
URP 是为"勾了后处理的那台相机"单独跑一次 pass，而不是对整个栈的最终合成结果跑。
一旦开在 Base 相机上，顺序就变成「画角色 → 对角色做后处理 → Overlay 把 UI 画在
已经后处理完的画面上」，角色像素进过后处理链、UI 像素没进，两者位移正比于像素
离屏幕中心的距离。由于相机跟随玩家，NPC 离屏幕中心的距离恰好等于玩家相对 NPC 的
位移，症状表现为「玩家往哪边走，NPC 头顶名字就往哪边偏，离得越远偏得越多」。

约束由 `Assets/_Project/Scripts/Rendering/SkyPrisonCameraStackPostProcessAuthority.cs`
在场景加载后强制执行，别在 Inspector 里改回去。

**不要用 TAA。** Spine 精灵图走的是自定义遮挡合成着色器，不写运动矢量，TAA 的历史
重投影没法正确工作。栈尾固定用 SMAA。

**`Camera.onPreCull` / `onPreRender` / `onPostRender` 在 URP 下永远不会触发**（内置
管线的回调）。需要逐相机回调时用 `RenderPipelineManager.beginCameraRendering`。
这也是唯一能拿到 Cinemachine 定位完成之后相机 Transform 的时机——`LateUpdate` 里读
到的永远是上一帧的旧值。

## 排查问题的方式

- **诊断数据自己取。** 加了 `Debug.Log` 之后从
  `C:\Users\admin\AppData\Local\Unity\Editor\Editor.log` 里 grep/awk 读回来，不要让
  用户手动复现并截图 Console。场景和资产的值直接读 YAML 文件，不要问 Inspector 里
  是什么。只让用户做他物理上非做不可的事（进 Play、操作角色、肉眼判断画面）。
- **设计能二分问题空间的实验，不要逐个验证猜想。** 动手前先问「这个实验的两种结果
  分别排除掉什么」，两边都答不上来就别做。
- **当所有能用代码读到的量都可证明地干净、画面却确实有问题时，立刻停止加更精细的
  数值日志，转去查渲染路径**（哪台相机、哪个层、进没进后处理 pass、有没有中间 RT）。
  这类 bug 在 CPU 端根本不存在。
- 警惕**恒等式伪证据**：正交投影下两个固定世界点的屏幕间距与相机平移无关，"间距
  恒定"是数学必然，不构成任何证据。下结论前先问这个量会不会恒定。

## 编译检查

Unity 开着的时候不要跑 batchmode 编译，直接让编辑器自动重编译即可。确实需要
batchmode 时先用 `tasklist //FI "IMAGENAME eq Unity.exe"` 确认 Unity 已关闭。
