# Andy ACP Examples

This directory contains examples demonstrating the ACP (Agent Client Protocol) implementation.

## Running the Examples

### Server Mode

The server listens for ACP messages on stdin and sends responses to stdout:

```bash
dotnet run --project Andy.Acp.Examples -- --server
```

The server will:
- Listen for ACP messages on stdin
- Echo received messages with timestamps
- Exit gracefully on EOF or Ctrl+C

### Client Mode

The client sends test ACP messages to stdout:

```bash
dotnet run --project Andy.Acp.Examples -- --client
```

The client will send a series of test messages in ACP format to stdout.

### Client-Server Communication

You can pipe the client output to the server input:

```bash
dotnet run --project Andy.Acp.Examples -- --client | dotnet run --project Andy.Acp.Examples -- --server
```

This will:
1. Client sends messages to stdout
2. Server receives messages from stdin
3. Server processes messages and sends responses to stdout

### Testing with the Test Script

A test script is provided to demonstrate ACP message formatting:

```bash
./test-acp.sh
```

This script shows how to properly format ACP messages with Content-Length headers.

## ACP Message Format

Messages follow the ACP/LSP format:

```
Content-Length: <byte-length>\r\n
\r\n
<json-content>
```

Example:
```
Content-Length: 36\r\n
\r\n
{"method":"ping","id":1,"jsonrpc":"2.0"}
```

## Notes

- The client writes diagnostic output to stderr and ACP messages to stdout
- The server can handle multiple messages in sequence
- Both client and server handle EOF gracefully
- Use Ctrl+C to stop either client or server