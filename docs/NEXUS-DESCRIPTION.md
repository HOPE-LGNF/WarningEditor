# Warning Editor

**把 Experiment 带回警告页，顺便重新排一遍。**
**Bring the Experiment back to your warning screen — and lay it out your way.**

---

## Description / 描述

**EN** — Warning Editor turns the content-warning screen shown at startup into a canvas. Move, resize, add and remove elements: images, the ORO / *casualties unknown* logo, the localized warning text, and your own text boxes. Everything is edited in-game from a panel added to the game's own Settings menu, with a live preview and one-click undo.

It also restores the **Experiment** from the demo build's splash screen — the creature that never made it into the retail release — as a regular element you can place anywhere.

**中文** — 警告页编辑器把启动时那页「本内容警告」变成一块画布。你可以移动、缩放、增删元素：图片、ORO / *casualties unknown* 标志、本地化警告文本，以及自己添加的文本框。所有操作都在游戏内完成——入口加在游戏自己的设置菜单里，带实时预览和撤回。

它还把 **Demo 启动画面里那只 Experiment**（正式版没有保留的那只）做成普通元素，想放哪就放哪。

---

## Installation instructions / 安装说明

**EN**
1. Install **BepInEx 5** (x64) into the game folder.
2. Extract the archive into the game root, so you end up with:
   `BepInEx/plugins/Warning Editor/WarningEditor.dll` and `BepInEx/plugins/Warning Editor/images/`.
3. Launch the game once — the config is generated at `BepInEx/config/com.hope.cu.warningeditor.cfg`.
4. In the main menu open **Settings → Game → Warning Layout → Open Editor**.

**中文**
1. 给游戏装好 **BepInEx 5**（x64）。
2. 把压缩包解压到游戏根目录，最终应为：
   `BepInEx/plugins/Warning Editor/WarningEditor.dll` 与 `BepInEx/plugins/Warning Editor/images/`。
3. 启动一次游戏，配置会生成在 `BepInEx/config/com.hope.cu.warningeditor.cfg`。
4. 主菜单进入 **设置 → 游戏 → 警告页布局 → 打开编辑器**。

---

## Main features / 主要功能

**EN**
- **Image elements** — 7 built-in presets plus any custom PNG/JPG dropped into the `images/` folder.
- **Text boxes** — native game font, per-selection coloring, horizontal and vertical alignment, adjustable font size.
- **Direct manipulation** — drag to move, drag the corner handle to resize (top-left stays anchored), arrow keys for 1 px, `Shift` for 10 px.
- **Guides & alignment** — snapping guides while dragging, plus top / bottom / left / right / horizontal-center / vertical-center buttons.
- **Undo** — `Ctrl+Z`, last 40 steps.
- **Preview mode** — hide every editing widget and look at the result; `Esc` to return.
- **Save confirmation** — nothing is written to disk until you press **Save**; `Esc` / Cancel asks first. Reset Layout does not auto-save and can be undone.
- **Bilingual UI** — the editor follows the game's language (Chinese / English).
- **Editable warning text** — override the original warning; leave it empty and it keeps following the game's language.

**中文**
- **图片元素** —— 内置 7 张预设，也可以把任意 PNG/JPG 丢进 `images/` 文件夹。
- **文本框** —— 使用游戏原生字体，支持选中选段染色、横向/纵向对齐、字号可调。
- **直接操作** —— 拖动移动，拖右下角缩放（左上角固定），方向键 1px、`Shift` 10px。
- **参考线与对齐** —— 拖动时自动吸附，另有贴顶/贴底/贴左/贴右/水平居中/垂直居中。
- **撤回** —— `Ctrl+Z`，保留最近 40 步。
- **预览模式** —— 隐藏所有编辑控件只看效果，`Esc` 返回。
- **保存确认** —— 不点「保存」绝不写盘；`Esc` / 取消 会先询问。恢复默认布局不会自动保存，且可以撤回。
- **中英界面** —— 跟随游戏语言自动切换。
- **可改原文** —— 可覆盖警告原文；留空则继续跟随游戏语言。

---

## Requirements / 前置需求

**EN**
- **Casualties Unknown 1.0** (Steam).
- **BepInEx 5.4.x (x64)** — required, the mod will not load without it.
- Windows. No other mods are required, and none are known to conflict.
- The mod works on the retail build only; the demo is **not** needed (the Experiment sprite is embedded).

**中文**
- **Casualties Unknown 1.0**（Steam 正式版）。
- **BepInEx 5.4.x（x64）** —— 必须安装，否则不会加载。
- Windows。不需要其他模组，目前也没有已知冲突。
- 只支持正式版；**不需要**装 Demo（实验体图案已内嵌在 DLL 里）。

---

## Shout outs / 致谢

**EN**
- **Orsoniks** — for Casualties Unknown, and for that warning screen worth redesigning.
- **BepInEx** and **HarmonyX** teams — without them none of this exists.
- Everyone who tested the early builds and reported the weird bugs (element hitboxes, tag leakage, off-screen text boxes).

**中文**
- **Orsoniks** —— 做出了《Casualties Unknown》，也做出了那页值得重新排的警告。
- **BepInEx** 与 **HarmonyX** 团队 —— 没有它们就没有这个模组。
- 所有帮忙测试早期版本、并报出那些奇怪 bug 的人（命中区遮挡、标签泄露、文本框跑到屏幕外）。

---

*Code is MIT. Images extracted from the game remain the property of their rights holders and are excluded from the license.*
*代码以 MIT 发布；从游戏提取的图片版权归原作者，不在 MIT 范围内。*
