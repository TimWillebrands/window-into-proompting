# 🎉 Proompting

A retro-themed AI chat application built with modern web technologies, featuring a Windows XP-style interface and real-time AI conversations powered by Google Gemini.

![Proompting Screenshot](https://via.placeholder.com/800x400/c0c0c0/000000?text=Proompting+Desktop)

## ✨ Features

- **🖥️ Windows XP Desktop Experience**: Complete retro desktop UI with draggable icons and windows
- **🤖 AI-Powered Chat**: Real-time conversations using Google Gemini 2.0 Flash model
- **💬 Party Rooms**: Create and join multiple chat rooms for organized conversations
- **⚡ Real-time Streaming**: Server-sent events (SSE) for live AI responses
- **🎨 Retro Styling**: Authentic Windows XP look with `xp.css` and custom Tailwind styling
- **📱 Interactive UI**: AlpineJS-powered interactivity with drag-and-drop functionality
- **🔄 HTMX Integration**: Dynamic content updates without full page refreshes
- **☁️ Cloudflare Workers**: Deployed on Cloudflare's edge network with Durable Objects

## 🛠️ Tech Stack

- **Backend**: [Hono](https://hono.dev/) with JSX
- **Frontend**: HTMX for state updates, AlpineJS for interactivity
- **Styling**: [xp.css](https://botoxparty.github.io/XP.css/) for retro Windows theme + Tailwind CSS
- **AI**: Google Gemini 2.0 Flash via `@google/genai`
- **Runtime**: Cloudflare Workers with Durable Objects for persistent chat rooms
- **Build Tools**: Wrangler, Tailwind CSS, Biome for linting

## 🚀 Quick Start

### Prerequisites

- Node.js 18+ or Bun
- Cloudflare account
- Google AI API key (Gemini)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/proompting.git
   cd proompting
   ```

2. **Install dependencies**
   ```bash
   npm install
   ```

3. **Set up environment variables**
   Create a `.dev.vars` file in the root directory:
   ```
   GEMINI_API_KEY=your_google_ai_api_key_here
   ```

4. **Generate TypeScript types**
   ```bash
   npm run cf-typegen
   ```

5. **Start development server**
   ```bash
   npm run dev
   ```

   This will start both the Wrangler dev server and Tailwind CSS watcher.

6. **Open your browser**
   Navigate to `http://localhost:8787` to see your retro desktop!

## 📦 Deployment

Deploy to Cloudflare Workers:

```bash
npm run deploy
```

Make sure to set your `GEMINI_API_KEY` as a secret in your Cloudflare Workers dashboard:

```bash
wrangler secret put GEMINI_API_KEY
```

## 🎮 Usage

1. **Launch the App**: Click on the desktop icons to open applications
2. **Open Chat**: Click the "Open Chat" icon to start a new conversation
3. **Create Party Room**: Enter a room name or let it generate one automatically
4. **Start Chatting**: Type your message and press "Send" or `Ctrl+Enter`
5. **Watch AI Respond**: See real-time streaming responses from Google Gemini
6. **Multiple Rooms**: Open multiple chat windows for different conversations

## 🏗️ Project Structure

```
proompting/
├── src/
│   ├── components/          # JSX components
│   │   ├── desktop.tsx     # Windows XP desktop interface
│   │   ├── party.tsx       # Chat room component
│   │   ├── message.tsx     # Message display components
│   │   ├── openParty.tsx   # Room creation dialog
│   │   ├── taskbar.tsx     # Windows taskbar
│   │   └── window.tsx      # Window container component
│   ├── index.tsx           # Main Hono application
│   └── party.ts            # Durable Object for chat persistence
├── public/
│   ├── img/                # Desktop icons
│   ├── input.css           # Tailwind CSS input
│   └── output.css          # Compiled CSS
├── wrangler.jsonc          # Cloudflare Workers configuration
└── package.json            # Dependencies and scripts
```

## 🔧 Available Scripts

- `npm run dev` - Start development server with file watching
- `npm run deploy` - Build and deploy to Cloudflare Workers
- `npm run cf-typegen` - Generate TypeScript types for Worker bindings

## 🎨 Customization

### Styling
- Modify `public/input.css` to add custom Tailwind styles
- The app uses `xp.css` for authentic Windows XP theming
- Components use a mix of utility classes and XP-style classes

### AI Model
- Currently uses Google Gemini 2.0 Flash
- Modify `src/party.ts` to change AI parameters or switch models
- Supports streaming responses for real-time chat experience

### Desktop Icons
- Add new icons to `public/img/`
- Modify `src/components/desktop.tsx` to add new applications

## 🤝 Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is open source and available under the [MIT License](LICENSE).

## 🙏 Acknowledgments

- [xp.css](https://botoxparty.github.io/XP.css/) for the authentic Windows XP styling
- [Hono](https://hono.dev/) for the lightweight web framework
- [HTMX](https://htmx.org/) for seamless HTML-over-the-wire updates
- [AlpineJS](https://alpinejs.dev/) for reactive frontend behavior
- Google Gemini for AI capabilities
- Cloudflare Workers for edge computing platform

---

Built with ❤️ using modern web technologies and a healthy dose of nostalgia for the Windows XP era.