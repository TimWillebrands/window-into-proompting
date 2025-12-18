# Proompting Party: Basic Strategies

## Context and Problem Statement

Nowadays we're all familiar with the _chat_ interface to LLM models. We get a little text input where you write a prompt, hit enter, and the model produces a stream of output tokens. At some point this stream will contain a _stop-token_ and the stream will end. Allowing you to _"respond"_ to the message just generated.
This UX somewhat emulates _normal_ chat interfaces we use among ourselves, PM's, whatsapp, etc... 

So this idea took hold of me.

_Most of these interfaces allow more than the one-on-one interactions, they allow us to converse in groups._

This is what I want to explore with [_Proompting Party_](https://proompting.party). What happens when we create a space that doesn't just let us speak to a single respondant? Groupchat's in our daily lives usualy exist for a couple of reasons, often to coordinate, which is pretty useless to do with LLMs. But there are more intriguing prospects.
In my work, what you might call ideation, or the work of looking at a proposed solution from multiple angles. By dragging in actual perspectives in the form of additional persons, often asynchronous in a groupchat. This is something you might emulate by allowing multiple voices. 

It will require us to colour these voices so they are actually distinct, which is somewhat easily done by adjusting the model's systemprompt. Instead of `you are a helpful assistant` we can provide something with a bit more personality to simulate a more personal perspective. These _perspectives_ are what I'll be calling _persona's_ in this project.
There are large issues with this naive approach, but the systemprompt is something I want to explore later. 

The most interesting incarnation of this concept is a groupchat where multiple users and persona's are simultaneously conversing/prompting. 

So with this context out of the way, what (technical) problems do we need to solve to get to a version 1? 

We have multiple problem domains such as **Authentication**: what is a user, **Model**: what powers our persona's, **Aesthetics**: what is the UI/UX we provide. But these aren't immediately interesting to me, instead what I'll focus on what I think is the most difficult problem to solve:  

**Coordination**: How can we best coordinate multiple streams of input being processed by the model, and orchestrate the conversation?


## Decision Drivers

So on this topic of _coordination_. What are the most important factors I want to consider when picking technology/patterns to facilitate programming this? For this we should first think about what difficulties we anticipate.


* **Curiousity and Fun**: (So far) this is a personal project. On top of exploring AI/LLM systems, I also want to explore technologies/concepts outside of my daily professional life and have fun with it.
* **Ease of programming model**: This whole thing is an experiment, I want to be able to iterate somewhat quickly if new ideas come up.
* **Deployment**: Although it can sometimes be fun, I don't want to explore orchestrating complex deployment pipelines in this project. Idealy the tech has a good story on this and gets out of the way.  


## Considered Options

* **Normal Server** Just use something like asp.net or Next.js. 
* Move the current follow-up function with persona-selection to before the prompt. And only recurse as 'follow up'

## Decision Outcome

Chosen option: Move current follow-up. Because there will be less similar pieces of code and the ordering is more intuitive.
