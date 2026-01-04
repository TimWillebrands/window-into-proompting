# Determine the responding Persona automatically

## Context and Problem Statement

Currently the user picks a persona from the list of personas. We want to determine the persona automatically based on the user's input.

## Considered Options

* Add additional functions to determine the persona automatically
* Move the current follow-up function with persona-selection to before the prompt. And only recurse as 'follow up'

## Decision Outcome

Chosen option: Move current follow-up. Because there will be less similar pieces of code and the ordering is more intuitive.
