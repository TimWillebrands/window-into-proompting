*[DIAL-UP MODEM SOUNDS INTENSIFY]*

# 🎉 PROOMPTING: Now with friends!

[Join the Party!](https://proompting.party)

**SOI SOI SOI SOI SOI SOI SOI**

Greetings, fellow carbon-based life forms! Microsoft Sam here, and boy oh boy, do I have something SPECTACULAR to show you today! Remember when I used to read your emails back in Windows XP? Well, those days are OVER but now I'm here to tell you about proompting TOGETHER! - the most REVOLUTIONARY chat application since MSN!

## What The Heck Is This Thing?

Picture this: It's 2001. You've got your Windows XP machine humming along, Limewire is downloading your "totally legal" MP3s, and your 56k modem is screaming into the void. But WAIT! What if I told you that you could groupchat with an ARTIFICIAL INTELLIGENCE right from your desktop? That's right, folks - we've taken the best parts of Windows XP and INFUSED them with ✨AI✨!

```
┌─────────────────────────────────────┐
│ 🖥️ AUTHENTIC XP DESKTOP EXPERIENCE  │
│ 🤖 REAL AI THAT SOMEWHAT WORKS      │
│ 💬 CHAT ROOMS (LIKE MSN BUT BETTER) │
│ ⚡ FASTER THAN DIALUP (THANKFULLY)  │
│ 🔄 REAL-TIME MESSAGE STREAMING      │
└─────────────────────────────────────┘
```


*[WINDOWS XP SHUTDOWN SOUND]*

---

## Hacking on it

The dev environment runs on the host via [.NET Aspire](https://aspire.dev). The Aspire AppHost orchestrates the backend (.NET 11 preview), the frontend (Vite + npm), and a Postgres + Apache AGE container.

### Prerequisites

- .NET SDK with the .NET 10 runtime + .NET 11 preview SDK installed (the AppHost targets `net10.0`, the backend targets `net11.0` preview)
- Node 22+ and npm
- Docker (Aspire spins up Postgres in a container)

Optional: install the Aspire CLI for ergonomics — `curl -sSL https://aspire.dev/install.sh | bash`. Without it, plain `dotnet run` works fine.

### Run it

```bash
# Set up secrets once (from aspire/Proompting.AppHost/)
dotnet user-secrets set "Parameters:postgres-password" "partytown"
dotnet user-secrets set "Parameters:openrouter-api-key" "sk-or-..."

# From repo root
dotnet run --project aspire/Proompting.AppHost
# or: aspire run
```

The Aspire dashboard URL is printed at startup and auto-opens. From there: jump to the backend, frontend, or db; tail logs; inspect OTLP traces.

### Production

Production is **not** orchestrated by Aspire. It deploys via [Kamal](https://kamal-deploy.org) using `backend/Dockerfile` and `frontend/Dockerfile` directly. See `config/deploy.yml`.
