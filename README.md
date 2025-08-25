+---------------------------------+
|COMRADE STEGANOV by blanc_chinois|
+---------------------------------+

Comrade Steganov is an open-source steganographical istrument based on LSB (Least Significant Bit) algorithm. It works with PNG images, preferably less than 10KB size due to specifics of extraction process. Text is injected into image after AES-256 encryption, no password/salt/iv stored on server. Project follows no user-log policy, debug messages do not contain user_id, chat_id or any other sensitive data. Most of error messages are sent to user as well.

Built with:
- .NET 8.0
- Python (numpy, pillow, grpc)
- Telegram.Bot API
- GRPC for microservices
- AES-256 (+ salt/iv) encryption

Commands for telegram bot:

| /hide - insert text into image     |
| /extract - extract text from image |
| /help - show help message          |
| /ping - check if the bot's alive   |


MIT license
