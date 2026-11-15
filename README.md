# Warning Editor

A BepInEx mod for **Casualties Unknown** that lets you redesign the content-warning screen shown at startup: add images, add text, and move or resize every element — with a live in-game editor.

> **English TL;DR** — Open `Settings → Game → Warning Layout → Open Editor`. Drag elements, add images from `images/`, add text boxes, undo with `Ctrl+Z`. Everything is saved to `BepInEx/config/com.hope.cu.warningeditor.cfg` only when you press **Save**.

---

## 一个给《Casualties Unknown》的警告页编辑器

把启动时那页「本游戏包含暴力…」变成你自己的：

- 图片元素：内置 7 张预设，支持自定义图片
- 文本框：原生字体，支持选区染色
- 拖拽移动、右下角缩放、方向键 1px 微调
- 对齐参考线 + 六向对齐 + 文本纵横对齐
- 撤回（`Ctrl+Z`）、恢复默认、保存确认
- 中/英界面，跟随游戏语言

---

## 安装 / Install

1. 安装 [BepInEx 5](https://github.com/BepInEx/BepInEx)（x64）
2. 把压缩包解压到游戏根目录：

```
Casualties Unknown/
└── BepInEx/
    ├── plugins/
    │   └── Warning Editor/
    │       ├── WarningEditor.dll
    │       └── images/
    └── config/
        └── com.hope.cu.warningeditor.cfg   (首次运行自动生成)
```

---

## 使用 / Usage

1. 主菜单 → **设置** → **游戏** → 最下面「警告页布局」→ **打开编辑器**
2. 顶部切换/添加元素，左侧改属性
3. 顶部「保存」才会写入配置；`Esc` / 取消 会询问是否保存

| 操作 | 效果 |
|---|---|
| 拖动元素 | 移动 |
| 拖右下角方块 | 缩放（左上角固定） |
| `↑↓←→` | 1px |
| `Shift + ↑↓←→` | 10px |
| `Tab` / `Shift+Tab` | 切换元素 |
| `Ctrl+Z` | 撤回 |
| `Esc` | 退出预览 / 取消（会询问保存） |

---

## 自定义图片 / Custom images

把 `.png` / `.jpg` 放进 `BepInEx/plugins/Warning Editor/images/`，在编辑器里选中图片元素后点「换图」循环切换，或「刷新图片」重新扫描。

- 透明背景 PNG 效果最好
- 按原始宽高比显示
- 加载时使用点采样（适合像素风）

---

## 配置 / Config

`BepInEx/config/com.hope.cu.warningeditor.cfg`

```ini
[General]
Enabled = true

[Elements]
Count = 1

[Element0]
Image = warning_experiment.png
X = 0
Y = -390
W = 407.4
H = 300

[TextBoxes]
Count = 0

[TextBox0]
Text = ...
Colors = 0-5:FF0000
VAlign = middle

[Text]
X = 0
Y = -313
W = 1782
H = 616
Override =        ; 留空 = 跟随游戏语言

[Logo]
X = 0
Y = 883.9
W = 453.6
H = 272.2
```

`Text` / `Logo` 的锚点是场景固定的（中心 / 底部中心）；图片与文本框的锚点是顶部中心，`Y` 为负值向下。

---

## 关于游戏原文 / Original warning text

内容框默认显示的是**游戏当前语言**下的 `other.discretionwarning`，mod 没有写死任何语言。
只有你改动它才会写入 `[Text] Override`；点「清除自定义文本」即可恢复跟随游戏语言。

---

## 构建 / Build

需要 .NET SDK（任意能编译 net461 的版本）。

```bash
# Windows
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Casualties Unknown"

# Linux / WSL
dotnet build -c Release -p:GameDir="/mnt/e/SteamLibrary/steamapps/common/Casualties Unknown"
```

输出在 `bin/Release/net461/WarningEditor.dll`。

---

## 实现 / How it works

- `PreRunScript.Start` Postfix：应用布局、创建图片/文本元素
- `SettingsMenu.SelectTab(Game)` Postfix：在游戏设置列表底部插入入口
- 编辑器使用独立的高排序 Canvas；按钮用矩形命中手动处理，不依赖 uGUI 射线层级
- 文字颜色以「纯文本 + 颜色区间」保存，渲染时才拼 `<color>` 标签，不会切坏标签
- 撤回基于配置快照（元素位置/尺寸/图片/文本/颜色）

---

## 素材说明 / Assets

默认的实验体图案取自 **Demo 构建**的 `splashlogo`（正式版没有这张图），已裁掉右侧 ORO 文字，仅保留实验体，以嵌入资源形式打包。

---

## License

MIT © 2026 HOPE-LGNF
