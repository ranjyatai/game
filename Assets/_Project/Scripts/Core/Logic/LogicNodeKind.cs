using UnityEngine;

public enum LogicNodeKind
{
    Leaf = 0,
    Container = 1,

    // 分支结构（If / If-Else）
    Branch = 2,

    // 循环结构（Loop / While）
    Loop = 3,

    // 注释节点
    Comment = 4,

    // 代码节点
    Code = 5
}
