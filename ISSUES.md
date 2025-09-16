MILESTONE 1 - decent chat-app
1) Stream stuff using yjs to clients so multiple clients can see the same data
2) Save yjs deltas (so chat history) to durable object
3) Make personas app and save persona's to KV
4) When starting chat pick a persona, save this in durable object of chat
5) Save opened chats to KV to display in openChat.tsx as _previous chats_

MILESTONE 2 - multi-user chat
1) Allow adding multiple personas to a chat, save this in durable object of chat
2) Add orchestrator which handles which persona answers a prompt
3) Login-system that facilitates different users to exist
