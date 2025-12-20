# Proompting Party: Basic Strategies

> Nota bene: I've stated work on this project a while back and only now decided I wanted to retroactively document my thought processes. I'll be doing this in the form of a series of [ADRs](https://adr.github.io/) with a less _formal_ more _blog_ tone of writing. I think it's a nice way to make _software architecture_ work more accessible and yap about my project at the same time.
> This is a post-rationalisation, sometimes you need to do this professionaly as well. But yes, my next ADR could very well be *why I moved off CloudFlare Workers*, that's part of the fun.

## Context and Problem Statement

Nowadays we're all familiar with the _chat_ interface to LLM models. We get a little text input where you write a prompt, hit enter, and the model produces a stream of output tokens. At some point this stream will contain a _stop-token_ and the stream will end. Allowing you to _"respond"_ to the message just generated.
This UX somewhat emulates _normal_ chat interfaces we use among ourselves, PM's, whatsapp, etc...

So this idea took hold of me.

_Most of these interfaces allow more than the one-on-one interactions, they allow us to converse in groups._

This is what I want to explore with [_Proompting Party_](https://proompting.party). What happens when we create a space that doesn't just let us speak to a single respondent? Groupchat's in our daily lives usually exist for a couple of reasons, often to coordinate, which is pretty useless to do with LLMs. But there are more intriguing prospects.
In my work, what you might call ideation, or the work of looking at a proposed solution from multiple angles. By dragging in actual perspectives in the form of additional persons, often asynchronous in a groupchat. This is something you might emulate by allowing multiple voices.

It will require us to colour these voices so they are actually distinct, which is somewhat easily done by adjusting the model's systemprompt. Instead of `you are a helpful assistant` we can provide something with a bit more personality to simulate a more personal perspective. These _perspectives_ are what I'll be calling _personas_ in this project.
There are large issues with this naive approach, but the systemprompt is something I want to explore later.

The most interesting incarnation of this concept is a groupchat where multiple users and personas are simultaneously conversing/prompting.

So with this context out of the way, what (technical) problems do we need to solve to get to a version 0.1?

We have multiple problem domains such as **Authentication**: what is a user, **Model**: what powers our personas, **Aesthetics**: what is the UI/UX we provide. But these aren't immediately interesting to me, instead what I'll focus on what I think is the most difficult problem to solve:

**Coordination**: How can we best coordinate multiple streams of input being processed by the model, and orchestrate the conversation?

### The Coordination challenge

So on this topic of _coordination_. While Auth and UI are solvable using standard patterns (although I won't solve UI in a standard way, but that's for another time), coordination will require a more thoughtful approach. 
Even in its simplest form, it will be a challenge, we're basically facilitating a digital dinner party with multiple conversations going on at the same time. Everyone potentially screaming through one another from all sides of the table, but perhaps that is the point. 
This is our avenue for experimentation and fun. Being able to iterate on coordination is a must have and there are two main challenges I identified:

**Who Speaks?**, we have various personas, how do we determine who is supposed to speak? There are multiple ways to approach this. The party might have a director process that reviews the flow of the conversation and decides who speaks next. Maybe all personas should have a concept of _attention_ to the conversation and decide themselves if they should respond.

Saving the **State of the Party**. The conversation is in a constant state of flux, with multiple participants. Again this problem domain can be approached from multiple directions. The most straightforward method seems to save all messages produced by the personas in a single store. However, maybe it makes more sense to save the state of each persona individually, this way the state of the party is the union of messages of each persona.

Framed in more technical terms the **coordination challenge** is in essence: _what processes get to run and when_, and _what shared state do they operate on_.

## Decision Drivers

- **Curiousity, Fun and Growth**: (So far) this is a personal passion project. On top of exploring AI/LLM systems, I want to explore technologies/concepts outside of my daily professional life and have fun with it. Growing my skills and knowledge to see how other technologies solve problems and if they are perhaps usable in my professional life.
- **Programming model lends itself to coordination**: This thing is an experiment, and I want to be able to iterate somewhat quickly if new ideas come up.
- **Ease of deployment**: Although sometimes be fun, I don't want to be orchestrating complex deployment pipelines in this project. Ideally we don't need a patchwork of databases, message-queues or other services.

## Considered Options

### Dotnet; a Server and a Database

C# and .NET are my professional weapons of choice. I've been using them for years and feel very comfortable I could solve any issues this project might encounter can be solved using the language and its vast ecosystem.

However, I'm not making a cold calculated business decision here, I personally want to explore other programming models and technologies that might be more suitable for this project.

I believe I could program this using just good old C# and postgres, which would cover the _programming model_ and _ease of deployment_. This would be the correct option in a business environment but I'm excited to explore other options.

### Elixir & Phoenix

Now we're talking! Elixir is a functional programming language that runs on the Erlang virtual machine. It's known for its concurrency and fault tolerance, which makes it a good fit for building a distributed system like this. Phoenix is a web framework built on top of Elixir that provides a lot of out-of-the-box functionality for building web applications.

I really enjoy writing functional code and if hackernews and t3 are to be believed Elixir is a great language.

But! I'm not sure if I want to learn a new language and framework just for this project. Even though learning is an explicit decision-driver, that mainly concerns learning about LLMs. Another language and framework is a tad excessive.

In addition to that, I'm not sure about the deployment strategy. It seems like many elixir projects deploy on fly.io, which is cool, but not something I feel like bothering with.

### Cloudflare Workers, Durable Objects

I've known about Cloudflare Workers for a while but never really had a use case for them. These are extremely low-weight functions ran on the cloudflare network. I've been using Azure Functions but Cloudflare Workers are a much more constrained and opinionated model, especially when you also use their _Durable Objects_ to store data. They are a very 'pure' serverless runtime and the persistence model seems very novel, exporing these concepts is very interesting to me. 

You program these workers in TypeScript, which I am very comfortable with although mostly in the browser. You could use compiled languages like Rust since these workers support WASM but I don't really see the point. 

The _deployment story_ is also really easy, cloudflare provides this CLI tool called wrangler which makes it very easy to deploy your project with a single command.

The main problem I have with these workers is the proprietary nature of the platform. If you want to get off CloudFlare you'll probably need to rewrite your codebase which in a business setting is a very large risk.
It should be noted that there are reports of customers of CloudFlare getting called into meetings with their sales teams and basically told they need very expensive contracts. Do your research if you decide to explore Workers professionally.

But for now, I'm going to stick with Cloudflare Workers and Durable Objects since I'm intrigued by the idea of a pure serverless runtime and the novel persistence model.

## Decision Outcome

Chosen option: **Cloudflare Workers & Durable Objects** because it seems like a great fit for our problem. The technology seems exciting and fun to explore, and I don't think I'll be bogged down with deployment hassles.

Thanks for reading!

**Next up**: Why I used a desktop-aesthetic design for the UI.
