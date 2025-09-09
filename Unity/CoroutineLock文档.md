# CoroutineLock 协程锁模块文档

## 概述

CoroutineLock是ET框架中的一个核心模块，用于管理协程的并发访问控制。它提供了一种机制来确保同一时间只有一个协程能够访问特定的资源或执行特定的操作，从而避免竞态条件和数据不一致的问题。

## 核心概念

### 1. 锁的类型 (CoroutineLockType)
- **None**: 无锁状态
- **Location**: 位置进程锁
- **MessageLocationSender**: 消息位置发送器锁
- **Mailbox**: 邮箱锁
- **UnitId**: 单位ID锁（用于地图服务器上下线）
- **DB**: 数据库锁
- **Resources**: 资源锁
- **ResourcesLoader**: 资源加载器锁

### 2. 锁的层次结构
```
CoroutineLockComponent (Scene级别)
├── CoroutineLockQueueType (按类型分组)
    ├── CoroutineLockQueue (按Key分组)
        ├── CoroutineLock (具体的锁实例)
        └── WaitCoroutineLock (等待队列)
```

## 工作原理

### 1. 锁的获取流程
1. 调用 `CoroutineLockComponent.Wait(type, key, timeout)` 请求锁
2. 系统查找或创建对应的 `CoroutineLockQueueType`
3. 在 `CoroutineLockQueueType` 中查找或创建 `CoroutineLockQueue`
4. 如果队列为空，直接创建并返回 `CoroutineLock`
5. 如果队列不为空，创建 `WaitCoroutineLock` 并加入等待队列

### 2. 锁的释放流程
1. `CoroutineLock` 被销毁时，调用 `Destroy` 方法
2. 在下一帧通过 `nextFrameRun` 队列处理下一个等待的协程
3. 通知对应的 `CoroutineLockQueue` 处理下一个等待者
4. 如果队列为空，清理相关资源

### 3. 超时机制
- 每个等待的协程都可以设置超时时间
- 超时后会自动抛出异常，避免死锁
- 超时检查通过 `TimerComponent` 实现

## 使用方法

### 1. 基本用法

```csharp
// 获取协程锁
CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.UnitId, unitId, 60000);

try
{
    // 执行需要加锁的操作
    await DoSomethingWithLock();
}
finally
{
    // 释放锁（自动释放，无需手动调用）
    // 当coroutineLock被销毁时，会自动通知下一个等待者
}
```

### 2. 带超时的用法

```csharp
// 设置5秒超时
CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.DB, dbKey, 5000);

try
{
    // 数据库操作
    await DatabaseOperation();
}
catch (Exception e)
{
    if (e.Message.Contains("coroutine is timeout!"))
    {
        Log.Warning("数据库操作超时");
    }
    throw;
}
```

### 3. 在组件中使用

```csharp
[ComponentOf(typeof(Unit))]
public class UnitComponent : Entity, IAwake
{
    public async ETTask DoSomething()
    {
        // 使用UnitId作为锁的key
        CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.UnitId, self.Id, 30000);
        
        try
        {
            // 执行需要同步的操作
            await ProcessUnitOperation();
        }
        finally
        {
            // 锁会在coroutineLock被销毁时自动释放
        }
    }
}
```

## 实际应用场景

### 1. 防止重复登录
```csharp
public async ETTask<bool> Login(long userId)
{
    // 使用用户ID作为锁的key，防止同一用户重复登录
    CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.UnitId, userId, 10000);
    
    try
    {
        // 检查用户是否已在线
        if (IsUserOnline(userId))
        {
            return false; // 用户已在线
        }
        
        // 执行登录逻辑
        await PerformLogin(userId);
        return true;
    }
    finally
    {
        // 锁自动释放
    }
}
```

### 2. 资源加载同步
```csharp
public async ETTask<GameObject> LoadResource(string resourcePath)
{
    // 使用资源路径作为锁的key，防止重复加载同一资源
    CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.Resources, GetHashCode(resourcePath), 30000);
    
    try
    {
        // 检查资源是否已加载
        if (IsResourceLoaded(resourcePath))
        {
            return GetLoadedResource(resourcePath);
        }
        
        // 加载资源
        GameObject resource = await LoadResourceAsync(resourcePath);
        CacheResource(resourcePath, resource);
        return resource;
    }
    finally
    {
        // 锁自动释放
    }
}
```

### 3. 数据库操作同步
```csharp
public async ETTask SavePlayerData(long playerId, PlayerData data)
{
    // 使用玩家ID作为锁的key，防止数据竞争
    CoroutineLock coroutineLock = await self.Scene<CoroutineLockComponent>().Wait(CoroutineLockType.DB, playerId, 15000);
    
    try
    {
        // 读取当前数据
        PlayerData currentData = await Database.ReadPlayerData(playerId);
        
        // 合并数据
        PlayerData mergedData = MergePlayerData(currentData, data);
        
        // 保存到数据库
        await Database.SavePlayerData(playerId, mergedData);
    }
    finally
    {
        // 锁自动释放
    }
}
```

## 性能考虑

### 1. 锁的粒度
- 锁的粒度越细，并发性越好，但管理复杂度越高
- 锁的粒度越粗，并发性越差，但管理简单
- 建议根据业务需求选择合适的锁粒度

### 2. 超时设置
- 超时时间不宜过长，避免长时间等待
- 超时时间不宜过短，避免不必要的超时异常
- 建议根据操作复杂度设置合理的超时时间

### 3. 锁的清理
- 系统会自动清理空的锁队列
- 超时的锁会自动抛出异常并清理
- 无需手动管理锁的生命周期

## 注意事项

### 1. 避免死锁
- 确保锁的获取顺序一致
- 避免在持有锁时再次请求同一类型的锁
- 合理设置超时时间

### 2. 异常处理
- 在try-finally块中使用锁
- 处理超时异常
- 确保锁能够正确释放

### 3. 性能优化
- 避免长时间持有锁
- 减少锁的持有时间
- 合理选择锁的类型和key

## 总结

CoroutineLock是ET框架中一个强大而灵活的并发控制机制。它通过类型和key的组合提供了细粒度的锁控制，支持超时机制，并且能够自动管理锁的生命周期。合理使用CoroutineLock可以有效避免并发问题，提高系统的稳定性和可靠性。

通过本文档的学习，开发者应该能够理解CoroutineLock的工作原理，掌握其使用方法，并在实际项目中正确应用这一机制来解决并发控制问题。 