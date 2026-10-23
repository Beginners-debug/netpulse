# 第三方资源与许可

NetPulse 本体以 MIT 许可发布（见 `LICENSE`）。程序内置了以下开源字体子集，它们各自保留原有许可。

## 内置字体

| 字体 | 用途 | 许可 | 来源 |
| --- | --- | --- | --- |
| **Fredoka**（wght 600） | 数字、速率、延迟等所有数值与拉丁字母 | SIL Open Font License 1.1 | https://fonts.google.com/specimen/Fredoka |
| **站酷快乐体 ZCOOL KuaiLe** | 中文标题、品牌字样、按钮文案 | SIL Open Font License 1.1 | https://fonts.google.com/specimen/ZCOOL+KuaiLe |

说明：

- 仓库只提交**子集化后**的字体文件（`assets/digitfont.ttf` ≈ 14 KB、`assets/cjkfont.ttf` ≈ 120 KB），
  原始字体放在 `tools/fontsrc/` 供重新生成使用，均为 OFL 授权，可随程序一起分发。
- 子集由 `tools/make-font.py` 生成，只保留界面里真正出现的字符，因此体积很小。
- OFL 要求在分发时保留版权与许可声明，本文件即承担该作用；如需完整许可文本，见
  https://openfontlicense.org/open-font-license-official-text/

## 图标

`assets/netpulse.ico` 与托盘动态图标均由程序内矢量绘制或本仓库自制，随 MIT 许可发布。

## 运行时依赖

无。NetPulse 只使用 .NET Framework 4.8 自带的 `System.Windows.Forms` / `System.Drawing` /
`System.Net.NetworkInformation` 等系统库，不引入任何第三方 NuGet 包。
