---
name: orleans-testing
description: How to do testing with Orleans and Orleans TestKit 
---


# Using Orleans.TestKit

**Orleans.TestKit** is a community-maintained library that provides a simulated grain activation context for unit-testing Microsoft Orleans grains in isolation. Instead of running a real Orleans silo, TestKit leverages Moq to generate test doubles for internal dependencies like persistent state, reminders, timers, streams, and the `IGrainFactory`.

## 1. Setting Up Your Test Class

To get started, inherit your test class from `TestKitBase`. This base class automatically provides a `TestKitSilo` via the `Silo` property, which you use to configure dependencies, mock state, and activate your grain.

```csharp
using Orleans.TestKit;
using Xunit;
using Moq;

public class MyGrainTests : TestKitBase
{
    // Tests go here
}
```

## 2. Core Testing Patterns

### 2.1. Activating the Grain Under Test

You should not instantiate your grain manually. Instead, use the `Silo.CreateGrainAsync<TGrain>(identity)` method to simulate a proper Orleans activation lifecycle.

```csharp
[Fact]
public async Task Activates_Grain_Successfully()
{
    // Arrange & Act
    var myGrain = await Silo.CreateGrainAsync<MyGrain>("my-grain-id");

    // Assert
    var result = await myGrain.DoSomething();
    Assert.True(result);
}
```

### 2.2. Injecting Mock Services

If your grain receives dependencies via constructor injection, add your mocked services to the `Silo.ServiceProvider` **before** activating the grain.

```csharp
[Fact]
public async Task Grain_Uses_Injected_Service()
{
    // Arrange
    var mockDbService = new Mock<IDatabaseService>();
    mockDbService.Setup(s => s.Save(It.IsAny<string>())).ReturnsAsync(true);
    
    // Inject the mock service into the TestKit silo
    Silo.ServiceProvider.AddService(mockDbService.Object);

    var grain = await Silo.CreateGrainAsync<MyGrain>("my-grain-id");

    // Act
    await grain.ProcessData("test data");

    // Assert
    mockDbService.Verify(s => s.Save("test data"), Times.Once);
}
```

### 2.3. Mocking Other Grains (Probes)

If the grain you are testing calls *other* grains via the `IGrainFactory`, use `AddProbe<T>(identity)` to inject a mock of the target grain into the test context's mock `IGrainFactory`.

```csharp
[Fact]
public async Task Grain_Calls_Another_Grain()
{
    // Arrange
    // AddProbe returns a Mock<T> representing the other grain
    var mockOtherGrain = Silo.AddProbe<IOtherGrain>("other-id");
    mockOtherGrain.Setup(x => x.FetchData()).ReturnsAsync("Mock Data");

    var grain = await Silo.CreateGrainAsync<MyGrain>("my-grain-id");

    // Act
    var result = await grain.CoordinateWork("other-id");

    // Assert
    Assert.Equal("Processed Mock Data", result);
    mockOtherGrain.Verify(x => x.FetchData(), Times.Once);
}
```

### 2.4. Mocking Persistent State

If your grain uses Orleans persistent state features (e.g., `IPersistentState<T>` injected in the constructor), use `Silo.AddPersistentState()` to inject state data before creation.

```csharp
[Fact]
public async Task Grain_Reads_State()
{
    // Arrange
    var initialState = new MyState { Count = 5 };
    Silo.AddPersistentState(initialState);

    var grain = await Silo.CreateGrainAsync<MyGrainWithState>("my-grain-id");

    // Act
    var currentCount = await grain.GetCount();

    // Assert
    Assert.Equal(5, currentCount);
}
```

### 2.5. Testing Timers and Reminders

Orleans.TestKit captures timers and reminders in local registries, avoiding actual asynchronous delays and allowing synchronous verification.

```csharp
[Fact]
public async Task Grain_Registers_Timer()
{
    // Arrange
    var grain = await Silo.CreateGrainAsync<MyGrain>("my-grain-id");

    // Act
    await grain.StartPeriodicWork();

    // Assert
    // Check the timer registry on the Silo to ensure the timer was created
    Assert.Single(Silo.TimerRegistry.Timers);
    var timer = Silo.TimerRegistry.Timers.First();
    
    // You can manually fire the timer in your tests to verify behavior
    await timer.FireAsync();
}
```

## 3. Caveats & Best Practices

1. **Isolation vs. Integration:** Orleans.TestKit simulates context; it does **not** spin up an actual Orleans runtime. It is strictly for fast, isolated, single-grain unit tests.
2. **Threading & Reentrancy:** The test kit does not simulate Orleans' single-threaded execution guarantees or reentrancy scheduling. Threading bugs in your grain may not be caught here.
3. **Hybrid Approach:** Mocks can couple tests to implementation details. It is recommended to use `Orleans.TestKit` for complex internal state verification and combine it with the official `InProcessTestCluster` for integration scenarios where you want the full Orleans message routing and serialization layers tested.
