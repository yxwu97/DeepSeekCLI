# 识微品牌与系统图标

“识微”通用 LOGO 使用已选定的 C 光形图案：不对称圆头四臂与右上独立微星。母版位于 `src/DeepSeekHarnessDesktop/Assets/Shiwei-Lingguang.svg`，品牌图形本身不含文字或系统角标。

## 系统区分规则

- 各系统使用同一主图形与微星，在圆角底的右下加入黑色斜角，配白色系统缩写。
- DeepSeek Harness Desktop 使用 `DSH`；其他系统替换为自身缩写。
- 角标不遮挡主形或右上微星，不改变品牌图形的比例、位置和颜色。
- 当前 256 × 256 坐标中，斜边连接 `(256,144)` 与 `(144,256)`，右下外缘沿用半径 54 的圆角；DSH 字样沿斜边向右上排列。
- 字样保存为矢量轮廓，避免缺少字体或字体替换导致显示差异。独立角标母版为 `src/DeepSeekHarnessDesktop/Assets/Shiwei-DSH-Badge.svg`。

主色为朱砂红 `#B94432`，反白为暖白 `#FFF6E9`，浅底为 `#F0E5D5`。黑色 `#000000` 用于系统斜角，字样为白色 `#FFFFFF`。

## 资源与生成

在 Windows 仓库根目录运行 `./eng/Generate-AppIcon.ps1`，生成 `output/shiwei-lingguang-icon-kit/` 及同名 ZIP。

| 资源 | 用途 |
| --- | --- |
| `app-cinnabar` / `app-ivory` | 无角标通用品牌图标，朱砂底／浅底 |
| `mark-cinnabar` / `mark-ivory` | 透明品牌图形及反白版 |
| `mark-black` / `mark-white` | 透明单色品牌图形 |
| `dsh-cinnabar` / `dsh-ivory` | DSH 系统图标，朱砂底／浅底 |

每版提供 SVG、11 个尺寸的 PNG，以及包含 16/20/24/32/40/48/64/128/256 像素九帧的 ICO。脚本将 `dsh-cinnabar` 写入应用的 `Assets/App.ico` 和 `Assets/App.png`，供 EXE、窗口及托盘使用。`dsh-preview.png` 展示通用版、系统版及原尺寸小图标。

16/20/24 像素的小图标中，系统缩写无法保证清晰阅读，识别主要依靠主图形、底色和黑色角标；应用名称和托盘提示继续承担文字识别。100%/125%/150% 下的 16 DIP 图标分别有 16/20/24 像素资源。

该规则是品牌视觉规范，不代表商标注册或近似检索已通过。
