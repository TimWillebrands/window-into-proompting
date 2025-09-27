MILESTONE 1 - decent chat-app
[ ] Stream stuff to clients so multiple clients using a fan-out can see the same data
[x] Save chat history to durable object
[ ] Make personas app and save persona's to KV
[ ] When starting chat pick a persona, save this in durable object of chat
[ ] Save opened chats to KV to display in openChat.tsx as _previous chats_

MILESTONE 2 - multi-user chat
1) Allow adding multiple personas to a chat, save this in durable object of chat
2) Add orchestrator which handles which persona answers a prompt
3) Login-system that facilitates different users to exist
4) Use YJS to better facilitate collaboration and real-time updates
