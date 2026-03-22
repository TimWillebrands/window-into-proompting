# AGENTS.md - Development Guide for Proompting

## Build & Development Commands

### Core Commands
- `bun run dev` - Start development server with hot reload (wrangler + tailwind watch)
- `bun run deploy` - Build and deploy to production with minification
- `bun run copy-assets` - Copy streaming-markdown-component to public/vendor/
- `bun run cf-typegen` - Generate Cloudflare Workers types

### Code Quality
- `npx @biomejs/biome check .` - Run linter and formatter checks
- `npx @biomejs/biome check --write .` - Auto-fix linting issues and format
- `npx @biomejs/biome lint .` - Lint only (no formatting)
- `npx @biomejs/biome format .` - Format only (no linting)

### Testing
- No test framework currently configured
- Use manual testing via `bun run dev` and browser testing
- Future: Add bun test or vitest when tests are implemented

### Single Test Pattern (when tests exist)
- `bun test path/to/specific.test.ts` - Run specific test file
- `bun test --grep "test name"` - Run tests matching pattern

## Code Style Guidelines

### Formatting & Linting (Biome)
- **Indentation**: 4 spaces (Biome configured)
- **Quotes**: Double quotes for JavaScript/TypeScript
- **Semicolons**: Required (Biome enforces)
- **File Extensions**: `.ts` for TypeScript, `.tsx` for React components
- **Import Organization**: Auto-organized by Biome (`source.organizeImports: on`)

### TypeScript Configuration
- **Target**: ESNext with modern features
- **Module System**: ESNext with bundler resolution
- **JSX**: React JSX with Hono JSX import source
- **Strict Mode**: Enabled
- **Path Aliases**: 
  - `@/*` → `./src/*`
  - `@proompting/*` → `./packages/*/src/*`

### Naming Conventions
- **Files**: kebab-case for utilities (`string-utils.ts`), PascalCase for components (`MyComponent.tsx`)
- **Components**: PascalCase (React/Hono JSX components)
- **Functions**: camelCase (`getUserData()`)
- **Variables**: camelCase (`userName`, `isAuthenticated`)
- **Constants**: UPPER_SNAKE_CASE (`API_BASE_URL`)
- **Types/Interfaces**: PascalCase (`UserType`, `ApiResponse`)

### Import Patterns
```typescript
// External dependencies first
import { Hono } from "hono";
import { OpenAI } from "openai";

// Internal workspace packages
import { CoreType } from "@proompting/core";
import { BackendService } from "@proompting/backend";

// Local imports
import { MyComponent } from "./components/my-component";
import { helperFunction } from "./utils/helpers";
```

## Architecture Patterns

### Project Structure
- **Monorepo**: Workspace-based with packages/* structure
- **Cloudflare Workers**: Primary deployment target
- **Hono**: Web framework for routing and HTTP handling
- **Durable Objects**: Stateful storage and WebSocket management
- **Alpine.js**: Client-side state management for UI interactions
- **htmx**: Server-side rendering with dynamic content updates

### Backend Patterns
- **Routes**: Use Hono with TypeScript generics for bindings
- **Durable Objects**: Extend `DurableObject<CloudflareBindings>` class
- **SQLite**: Use `ctx.storage.sql` for persistence
- **WebSockets**: Implement hibernatable connections for real-time
- **Error Handling**: Return proper HTTP status codes (400, 404, 500)
- **Validation**: Check inputs at route level, use TypeScript types

### Frontend Patterns (XP Desktop UI)
- **Windows XP Theme**: xp.css library for authentic styling
- **Desktop Metaphor**: Apps open in draggable, resizable windows
- **Alpine.js State**: Reactive UI state with `$persist()` for localStorage
- **htmx Integration**: Use `hx-get`, `hx-post` with `hx-target` for updates
- **Component Structure**: WindowContainer wraps app content
- **Split Layouts**: Common pattern for list/detail views

### Database & State
- **Durable Objects SQLite**: Primary data storage
- **Migrations**: Use `ctx.blockConcurrencyWhile()` for schema changes
- **Parameterized Queries**: Prevent SQL injection
- **Client State**: Alpine.js for UI, server state via htmx requests
- **Real-time**: WebSocket pub/sub pattern with typed messages

## Windows XP UI Guidelines

### Essential Classes
- `window` - Main window container
- `title-bar` / `title-bar-text` - Window headers
- `window-body` - Content area
- `field-row` - Form element grouping
- `status-bar` / `status-bar-field` - Status information
- `tree-view` - Hierarchical lists

### Window Management
- Apps load as `<WindowContainer>` components via htmx
- Prevent duplicates: `hx-trigger="click[!document.getElementById('app-id')]"`
- Persist positions: Alpine.js `$persist()` stores window state
- Drag/resizable: CSS `resize` class + Alpine.js handlers

### Design Principles
- **Semantic HTML**: Use proper `<button>`, `<input>`, `<label>` elements
- **XP.css First**: Let xp.css handle Windows XP styling
- **Tailwind for Layout**: Spacing, positioning, responsive design
- **No Custom Styles**: Avoid overriding XP component styles

## Error Handling & Validation

### Backend
```typescript
// Input validation
if (!request.body || typeof request.body !== "object") {
    return c.text("Invalid request", 400);
}

// Error responses
try {
    const result = await operation();
    return c.json(result);
} catch (error) {
    console.error("Operation failed:", error);
    return c.text("Internal server error", 500);
}
```

### Frontend
- Use htmx events for loading states: `hx-on:before-request`, `hx-on:after-request`
- Alpine.js for UI feedback: `x-bind:class="loading ? 'opacity-50' : ''"`
- Graceful degradation with fallback content

## Security & Best Practices

### Cloudflare Workers
- Never commit secrets to repository (use wrangler secrets)
- Validate all user inputs at route boundaries
- Use proper CORS headers for API endpoints
- Implement rate limiting for sensitive operations

### TypeScript
- Avoid `any` types - use proper typing or `unknown`
- Use type guards for runtime type checking
- Leverage CloudflareBindings type for environment access
- Parameterize all SQL queries to prevent injection

### Frontend Security
- Escape user-generated content before rendering
- Use CSP headers for production
- Validate form data on both client and server
- Implement proper authentication checks

## Development Workflow

### Before Committing
1. Run `npx @biomejs/biome check --write .` to fix issues
2. Run `npx @biomejs/biome check .` to ensure no remaining issues
3. Test manually with `bun run dev`
4. Check that all TypeScript types compile

### Package Management
- Use Bun as package manager
- Workspace dependencies with `workspace:*` protocol
- External dependencies locked via bun.lock
- Run `bun install` after package.json changes

### Deployment
- Production builds use `wrangler deploy --minify`
- Assets processed via tailwindcss pipeline
- Type generation with `cf-typegen` for Cloudflare types
- Environment variables managed via wrangler.jsonc configuration