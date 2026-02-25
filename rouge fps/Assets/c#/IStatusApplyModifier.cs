/// <summary>
/// 异常施加修饰器：用于在 StatusContainer.ApplyStatus 入口处统一修改请求。
/// </summary>
public interface IStatusApplyModifier
{
    /// <summary>
    /// 优先级：数值越大越先执行（用于保证“最高优先级异常 Perk”先改 stacks）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 修改本次异常施加请求（可直接改 stacksToAdd 等字段）
    /// </summary>
    void Modify(StatusContainer target, ref StatusApplyRequest req);
}