// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Presentation taxonomy only. Preset indices and persisted identities stay unchanged.
internal static class EcgChooserGroups
{
    internal static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(new[]
    {
        "窦性心律", "房性早搏", "房性逸搏与自主心律", "心房扑动", "心房颤动",
        "交界性早搏", "交界性自主心律", "室上性心动过速", "室性早搏", "室性自主心律",
        "室性心动过速", "心室扑动与颤动", "二度房室传导阻滞", "三度房室传导阻滞",
        "室内传导阻滞", "预激与 PR 变异", "静止与电机械分离", "心房形态", "心室肥厚形态",
        "T 波形态", "钾相关形态", "钙相关形态", "洋地黄样形态", "奎尼丁样形态",
        "下壁梗死形态", "侧壁梗死形态", "前间壁梗死形态", "前壁梗死形态", "广泛前壁梗死形态"
    });
    internal static string For(int index) => index switch
    {
        0 or 1 or 3 => "窦性心律",
        4 or 40 or 41 => "房性早搏",
        6 or 7 or >= 66 and <= 71 => "心房颤动",
        8 or 9 or 37 or 38 or 39 => "心房扑动",
        31 or 36 => "房性逸搏与自主心律",
        5 or 42 or 43 => "交界性早搏",
        32 => "交界性自主心律",
        >= 23 and <= 25 => "室上性心动过速",
        >= 10 and <= 17 => "二度房室传导阻滞",
        18 or 19 => "三度房室传导阻滞",
        2 or >= 44 and <= 52 => "室性早搏",
        >= 26 and <= 30 => "室性心动过速",
        20 or 21 or 22 => "心室扑动与颤动",
        >= 33 and <= 35 => "室性自主心律",
        >= 53 and <= 58 => "室内传导阻滞",
        >= 59 and <= 65 => "预激与 PR 变异",
        >= 72 and <= 74 => "静止与电机械分离",
        >= 75 and <= 81 or 98 => "钾相关形态",
        >= 82 and <= 86 => "钙相关形态",
        >= 87 and <= 89 => "洋地黄样形态",
        >= 90 and <= 97 => "奎尼丁样形态",
        >= 99 and <= 101 => "心房形态",
        >= 102 and <= 106 => "心室肥厚形态",
        >= 107 and <= 114 => "T 波形态",
        >= 115 and <= 124 => "下壁梗死形态",
        >= 125 and <= 134 => "侧壁梗死形态",
        >= 135 and <= 144 => "前间壁梗死形态",
        >= 145 and <= 154 => "前壁梗死形态",
        >= 155 and <= 164 => "广泛前壁梗死形态",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
