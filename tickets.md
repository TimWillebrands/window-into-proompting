# Frontend Refactoring Plan (Revised)

This document outlines the plan to refactor the frontend of the Proompting Party application to address the growing complexity of its state management.

## Core Problem: State Duality

The fundamental issue is a **state duality** between Alpine.js and htmx.

1.  **Alpine.js State:** The `Desktop` component initializes an Alpine.js data object (`x-data`) that holds a `windows` object. This object is persisted to `localStorage` and is intended to be the single source of truth for the application's windows. A `<template x-for>` is set up to render windows from this state.

2.  **DOM State:** In practice, new windows are not created by manipulating the Alpine `windows` object. Instead, htmx is used to directly fetch a new HTML fragment from the server and append it to the DOM (e.g., `hx-get="/welcome" hx-swap="beforeend"`).

This creates two conflicting sources of truth: the Alpine `windows` object and the actual state of the DOM. The Alpine state is almost immediately stale and incorrect, and the UI is not reproducible from the application's state. This leads to fragile, unpredictable behavior and is difficult to debug and maintain.

## Other Pain Points

*   **Imperative DOM Probing:** The application relies on querying the DOM to make stateful decisions (e.g., `hx-trigger="click[!window.document.getElementById('welcome')]" `). This is an anti-pattern that couples components to the global DOM structure.
*   **Global Namespace Pollution:** Component state is being stored on the global `window` object (e.g., `window.currentModel`), which is not scalable and will cause conflicts between multiple instances of the same component.
*   **Inconsistent Component Lifecycle:** Components are created imperatively via htmx swaps, rather than declaratively through state changes, making lifecycle management difficult.

## Revised Solution: Unidirectional Data Flow with a Single Source of Truth

We will refactor the application to enforce a unidirectional data flow where the Alpine.js store is the **single source of truth** for the desktop state.

1.  An action (e.g., clicking an icon) will **not** use htmx to fetch a window. Instead, it will call a function in the central Alpine.js store.
2.  The Alpine.js store will update its `windows` state object, adding a new entry for the window.
3.  Alpine's reactive `x-for` loop will automatically detect the state change and render a new, minimal window container element into the DOM.
4.  This new container element will have an `hx-trigger="load"` attribute, which will then fire a *single* htmx request to fetch the window's *internal content* from the server.

This creates a clean, predictable, and maintainable architecture.

---

### Ticket 1: Create a Centralized Alpine.js Store

**Task:** Move the `x-data` from the `Desktop` component into a global, reusable Alpine.js store. This store will contain the `windows` object, `focusedApp`, `user`, etc., and methods for manipulating this state (e.g., `openWindow`, `closeWindow`).

**Justification:** A global store provides a single, accessible source of truth for all components. It decouples the state from a single DOM element and makes it easier to manage and interact with from any part of the application.

---

### Ticket 2: Refactor Window Creation to be State-Driven

**Task:**
1.  Remove the `hx-get` attributes from the desktop icons.
2.  Change the icons' `x-on:click` handlers to call the new `openWindow` method in the Alpine.js store.
3.  Ensure the `x-for` loop in the `Desktop` component correctly renders a placeholder for the new window when the `windows` object changes.
4.  The rendered placeholder will use `hx-trigger="load"` and `hx-get` to fetch its own content.

**Justification:** This is the most critical change. It fixes the state duality problem by ensuring all windows are created through state changes in the Alpine store. This makes the UI a direct reflection of the state, which is the core principle of reactive programming.

---

### Ticket 3: Eliminate Global Variables via Alpine's Event System

**Task:**
1.  Remove the custom `window.eventBus` object.
2.  Refactor the `party.tsx` component to use Alpine's native `$dispatch` magic property to send a `model-selected` event.
3.  The `select` element will now use `x-on:change="$dispatch('model-selected', { model: $event.target.value })"`.

**Justification:** Instead of creating a custom event bus, we will leverage Alpine.js's built-in event system (`$dispatch` and `x-on`). This is the idiomatic way to handle component communication in Alpine. It's more maintainable, requires less custom code, and is immediately understandable to anyone familiar with the framework. This change removes the last of the global variable anti-patterns.

---

### Ticket 4: Simplify htmx Triggers

**Task:** Remove DOM-based checks from htmx triggers like `hx-trigger="click[!window.document.getElementById('welcome')]"`.

**Justification:** With the new state-driven approach, this check is no longer necessary. The `openWindow` function in the Alpine store will be responsible for checking if a window already exists before creating a new one, using the `windows` state object as the source of truth. This makes the logic more robust and removes the component's dependency on the global DOM structure.
