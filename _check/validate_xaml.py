"""检查 XAML 与后台代码的一致性：控件名、事件处理函数、XML 合法性。

正式构建由 Visual Studio 完成，这里提前把能静态发现的问题挑出来。
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

EVENTS = {
    "Click", "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseDown", "MouseUp",
    "DoubleClick", "Loaded", "SelectionChanged", "TextChanged", "Checked",
    "Unchecked", "Closing", "Closed", "KeyDown", "KeyUp", "GotFocus", "LostFocus",
    "PreviewKeyDown", "SizeChanged", "MouseEnter", "MouseLeave",
}

# Fill 只属于 Shape，Background 只属于面板/控件/边框，这两个最容易写混
SHAPES = {"Rectangle", "Ellipse", "Path", "Polygon", "Polyline", "Line"}

# Foreground 是 Control 的属性，Border / 各种面板没有
NO_FOREGROUND = {
    "Border", "Grid", "StackPanel", "WrapPanel", "DockPanel", "Canvas",
    "Viewbox", "Image", "Rectangle", "Ellipse",
}

problems = []


def check_xml(path):
    try:
        tree = ET.parse(path)
        return tree
    except Exception as exc:
        problems.append("%s: XML 不合法 -> %s" % (path, exc))
        return None


def main(root):
    for base, _dirs, files in os.walk(root):
        if "obj" in base or "bin" in base:
            continue
        for name in sorted(files):
            if not name.endswith(".xaml"):
                continue
            xaml_path = os.path.join(base, name)
            code_path = xaml_path + ".cs"
            tree = check_xml(xaml_path)
            if tree is None:
                continue

            names = set()
            handlers = []
            for elem in tree.iter():
                tag = elem.tag.split("}")[-1]
                for key, value in elem.attrib.items():
                    if key == X + "Name":
                        names.add(value)
                    elif key in EVENTS:
                        handlers.append((key, value, tag))
                    elif key == "Fill" and tag not in SHAPES:
                        problems.append("%s: <%s Fill=...> 无效，非图形元素应该用 Background" % (name, tag))
                    elif key == "Background" and tag in SHAPES:
                        problems.append("%s: <%s Background=...> 无效，图形元素应该用 Fill" % (name, tag))
                    elif key == "Foreground" and tag in NO_FOREGROUND:
                        problems.append("%s: <%s Foreground=...> 无效，该元素没有 Foreground 属性" % (name, tag))

            code = ""
            if os.path.exists(code_path):
                with open(code_path, "r", encoding="utf-8-sig") as handle:
                    code = handle.read()
            else:
                problems.append("%s: 缺少后台代码文件" % xaml_path)

            for evt, handler, tag in handlers:
                pattern = r"(private|public|protected|internal)?\s*void\s+%s\s*\(" % re.escape(handler)
                if not re.search(pattern, code):
                    problems.append("%s: <%s %s=\"%s\"> 找不到对应方法" % (xaml_path, tag, evt, handler))

            # 后台代码里用到的控件名，必须在 XAML 中声明
            for match in re.finditer(r"\b([A-Z][A-Za-z0-9_]*)\s*\.\s*[A-Z][A-Za-z0-9_]*\s*=", code):
                ident = match.group(1)
                if ident in ("State", "Data", "Settings", "App", "Math", "String", "Grid", "Color"):
                    continue
                if ident not in names:
                    line = code[: match.start()].count("\n") + 1
                    problems.append("%s(%d): 后台用到的 %s 未在 XAML 中声明" % (os.path.basename(code_path), line, ident))

            print("%-28s 控件 %2d 个，事件 %d 个" % (name, len(names), len(handlers)))

    if problems:
        print("\n发现问题：")
        for item in problems:
            print("  - " + item)
        return 1
    print("\nXAML 检查通过")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
