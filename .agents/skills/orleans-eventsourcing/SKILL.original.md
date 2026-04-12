---
name: event-sourced-orleans
description: Design and implement event-sourced systems using Microsoft Orleans JournaledGrain APIs. Use when building Orleans grains that store state through events, implement command handling, and maintain CQRS-style read models or projections.
metadata:
  author: generated
  version: "1.0"
---

# Event-Sourced Orleans

This skill guides agents to implement **event sourcing in Microsoft Orleans** using the built-in `JournaledGrain` APIs.

Use this skill when:

- Designing **domain aggregates as Orleans grains**
- Persisting **events instead of mutable state**
- Implementing **CQRS-style command and read model separation**
- Building **projections or read models from grain events**

This skill follows the design described in the official Orleans documentation:

https://learn.microsoft.com/en-us/dotnet/orleans/grains/event-sourcing/

---

# Core Principles

When implementing event sourcing in Orleans:

1. **Grains represent aggregates**
2. **Commands raise events**
3. **Events mutate state**
4. **State is derived from the event log**
5. **Read models are projections**

Never mutate state directly.

State must only change via **events**.

---

# Key Orleans APIs

The primary base class is:

```csharp
JournaledGrain<TState, TEvent>
```

Important members:

| API | Purpose |
|----|----|
| `State` | Current aggregate state |
| `Version` | Number of confirmed events |
| `RaiseEvent(event)` | Append an event |
| `ConfirmEvents()` | Persist pending events |
| `RaiseEvents(events)` | Append multiple events |
| `RaiseConditionalEvent(event)` | Append with optimistic concurrency |
| `OnStateChanged()` | Hook when state updates |

---

# Grain Design Rules

## 1. One Aggregate Per Grain

Each grain should represent **one domain aggregate**.

Examples:

Good:

```
OrderGrain
UserAccountGrain
ShoppingCartGrain
InventoryItemGrain
```

Bad:

```
SystemGrain
DatabaseGrain
EverythingGrain
```

Grains should own their **complete aggregate state**.

---

# Step 1: Define Events

Events represent **facts that happened**.

They must be immutable and serializable.

Example:

```csharp
public interface IAccountEvent {}

public record AccountCreated(string Owner) : IAccountEvent;

public record MoneyDeposited(decimal Amount) : IAccountEvent;

public record MoneyWithdrawn(decimal Amount) : IAccountEvent;
```

Rules:

- Events describe **what happened**
- Events should **not contain behavior**
- Events should be **append-only**

---

# Step 2: Define Aggregate State

State is a **projection of events**.

It must implement `Apply` methods for each event.

```csharp
public class AccountState
{
    public string Owner { get; private set; }
    public decimal Balance { get; private set; }

    public void Apply(AccountCreated e)
    {
        Owner = e.Owner;
        Balance = 0;
    }

    public void Apply(MoneyDeposited e)
    {
        Balance += e.Amount;
    }

    public void Apply(MoneyWithdrawn e)
    {
        Balance -= e.Amount;
    }
}
```

Important rules:

- State must have a **parameterless constructor**
- State must **not contain business logic**
- State only **applies events**

---

# Step 3: Implement the Grain

The grain handles **commands** and raises events.

```csharp
public interface IAccountGrain : IGrainWithGuidKey
{
    Task CreateAccount(string owner);
    Task Deposit(decimal amount);
    Task Withdraw(decimal amount);
    Task<decimal> GetBalance();
}
```

Implementation:

```csharp
public class AccountGrain
    : JournaledGrain<AccountState, IAccountEvent>,
      IAccountGrain
{
    public async Task CreateAccount(string owner)
    {
        RaiseEvent(new AccountCreated(owner));
        await ConfirmEvents();
    }

    public async Task Deposit(decimal amount)
    {
        RaiseEvent(new MoneyDeposited(amount));
        await ConfirmEvents();
    }

    public async Task Withdraw(decimal amount)
    {
        if (State.Balance < amount)
            throw new InvalidOperationException("Insufficient funds");

        RaiseEvent(new MoneyWithdrawn(amount));
        await ConfirmEvents();
    }

    public Task<decimal> GetBalance()
    {
        return Task.FromResult(State.Balance);
    }
}
```

Important rules:

- Commands validate business rules
- Commands raise events
- Commands confirm events
- Queries read from `State`

---

# Event Persistence

When events are raised:

```
Command
  ↓
RaiseEvent()
  ↓
Event appended to log
  ↓
Apply() updates state
  ↓
ConfirmEvents() persists
```

After persistence:

```
State == replay(all events)
```

---

# Handling Multiple Events

If a command emits multiple events:

```csharp
RaiseEvents(new IAccountEvent[]
{
    new WithdrawalRequested(amount),
    new WithdrawalCompleted(amount)
});

await ConfirmEvents();
```

This writes events **atomically**.

---

# Optimistic Concurrency

Use conditional events to prevent conflicts.

```csharp
bool success = await RaiseConditionalEvent(
    new MoneyWithdrawn(amount)
);

if (!success)
{
    throw new Exception("Concurrent update detected");
}
```

This ensures:

```
event only commits if version matches
```

---

# State Change Hooks

You can react to state updates:

```csharp
protected override void OnStateChanged()
{
    // Example:
    // publish domain event
    // update external projection
}
```

Use this for:

- publishing events to streams
- updating read models
- triggering workflows

---

# CQRS Pattern

Event-sourced Orleans systems should follow **CQRS**.

## Command Side

Command grains:

```
Receive command
Validate rules
Raise events
Persist events
Update state
```

Example:

```
AccountGrain
OrderGrain
CartGrain
```

These grains **own the event log**.

---

## Read Models

Read models are projections.

They exist to support **queries**.

Examples:

```
AccountBalanceView
OrderSummaryView
UserDashboardView
```

They may be implemented as:

- Orleans grains
- database tables
- search indexes
- caches

---

# Projection Pattern

A common pattern is:

```
Command Grain
     │
     ▼
 Event Stream
     │
     ▼
 Projection Grain
     │
     ▼
 Read Model Storage
```

Example projection grain:

```csharp
public class AccountProjectionGrain : Grain
{
    private decimal balance;

    public Task Apply(MoneyDeposited e)
    {
        balance += e.Amount;
        return Task.CompletedTask;
    }

    public Task<decimal> GetBalance()
    {
        return Task.FromResult(balance);
    }
}
```

---

# Event Ordering Guarantees

Orleans guarantees:

```
events from a single grain are ordered
```

Therefore projections should process events **in order**.

Never process events out-of-order.

---

# Event Replay

When a grain activates:

```
load event log
replay events
rebuild state
activate grain
```

Agents must assume:

```
State is always derived from events
```

---

# When NOT to Use Event Sourcing

Avoid event sourcing when:

- state is trivial
- no history is needed
- write volume is extremely high
- the domain has no meaningful events

Prefer normal Orleans state grains instead.

---

# Common Mistakes

## Mutating State Directly

Bad:

```csharp
State.Balance += amount;
```

Correct:

```csharp
RaiseEvent(new MoneyDeposited(amount));
```

---

## Large Grain State

Grain state should remain **small**.

Large collections should be:

- separate grains
- external read models

---

## Using Events for Queries

Events are **not a query API**.

Queries must read:

- state
- projections
- read models

---

# Recommended Architecture

A typical system:

```
Clients
   │
   ▼
Command Grains
   │
   ▼
Event Log
   │
   ▼
Projection Grains
   │
   ▼
Read Models
```

This architecture provides:

- strong consistency per aggregate
- scalable reads
- full history
- replay capability

---

# Summary

Event-sourced Orleans systems follow this flow:

```
Command
   ↓
Grain validates
   ↓
RaiseEvent()
   ↓
Persist event
   ↓
Apply() updates state
   ↓
Projections update read models
```

Key rules:

- grains = aggregates
- commands raise events
- state applies events
- projections power queries

This pattern enables scalable, reliable distributed systems using Orleans.
