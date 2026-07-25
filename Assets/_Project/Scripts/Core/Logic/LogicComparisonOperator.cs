public enum LogicComparisonOperator
{
    [UnityEngine.InspectorName(">")]  Greater      = 0,
    [UnityEngine.InspectorName("<")]  Less         = 1,
    [UnityEngine.InspectorName("==")] Equal        = 2,
    [UnityEngine.InspectorName("!=")] NotEqual     = 3,
    [UnityEngine.InspectorName(">=")] GreaterOrEqual = 4,
    [UnityEngine.InspectorName("<=")] LessOrEqual  = 5,
    [UnityEngine.InspectorName("===")] StrictEqual = 6
}
