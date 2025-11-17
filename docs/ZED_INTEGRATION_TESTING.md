# Zed Editor Integration Testing Guide

This document provides a comprehensive guide for testing the Andy ACP implementation with Zed editor.

## Prerequisites

1. **Zed Editor** installed (download from [zed.dev](https://zed.dev/))
2. **.NET 9.0 SDK** or later
3. **andy-acp repository** cloned and built

## Setup

### 1. Build the Example

```bash
cd examples/Andy.Acp.Examples
dotnet build
```

### 2. Configure Zed

**Configuration file location:**
- macOS/Linux: `~/.config/zed/settings.json`
- Windows: `%APPDATA%\Zed\settings.json`

**Add to your Zed settings:**

```json
{
  "assistant": {
    "version": "2",
    "provider": {
      "name": "acp",
      "servers": [
        {
          "name": "andy-acp-example",
          "command": "dotnet",
          "args": [
            "run",
            "--project",
            "/absolute/path/to/andy-acp/examples/Andy.Acp.Examples",
            "--",
            "--server"
          ],
          "env": {},
          "description": "Andy ACP Example Server"
        }
      ]
    }
  }
}
```

**IMPORTANT**: Replace `/absolute/path/to/andy-acp` with your actual repository path.

### 3. Verify Configuration

Test the server manually before connecting with Zed:

```bash
cd examples/Andy.Acp.Examples

# Test tools/list
echo '{"jsonrpc":"2.0","method":"tools/list","id":1,"params":{}}' | dotnet run -- --server

# Expected output should include tool definitions for:
# - echo
# - calculator
# - get_time
# - reverse_string
```

## Testing Checklist

### 1. Connection Establishment

- [ ] Open Zed Editor
- [ ] Open Assistant Panel (`Cmd+?` on macOS, `Ctrl+?` on Windows/Linux)
- [ ] Verify connection status (should show connected to "andy-acp-example")
- [ ] Check for no error messages in Zed logs

**Troubleshooting:**
- If connection fails, check stderr output in terminal
- Verify `dotnet` is in your PATH
- Ensure all paths in settings.json are absolute

### 2. Initialization Handshake

The following should happen automatically (check server stderr logs):

- [ ] Zed sends `initialize` request
- [ ] Server responds with capabilities (tools, resources, logging)
- [ ] Zed sends `initialized` notification
- [ ] Session becomes active
- [ ] No JSON-RPC errors reported

**Expected stderr logs:**
```
[SERVER] Processing JSON-RPC request: initialize (ID: 1)
[SERVER] Created new session: {session-id}
[SERVER] Processing JSON-RPC request: initialized
[SERVER] Session {session-id} is now active
```

### 3. Tool Discovery

- [ ] Server advertises 4 tools in capabilities
- [ ] Zed can send `tools/list` request (if supported by Zed)
- [ ] All tools have proper JSON schemas

**Test manually:**
```bash
echo '{"jsonrpc":"2.0","method":"tools/list","id":2,"params":{}}' | dotnet run -- --server
```

**Expected response structure:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "tools": [
      {
        "name": "echo",
        "description": "Echoes back the provided text",
        "inputSchema": {
          "type": "object",
          "properties": {...},
          "required": ["text"]
        }
      },
      // ... other tools
    ]
  }
}
```

### 4. Tool Execution Tests

#### Test 1: Echo Tool

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 3,
  "params": {
    "name": "echo",
    "parameters": {
      "text": "Hello from Zed!"
    }
  }
}
```

**Expected result:**
```json
{
  "result": {
    "echo": "Hello from Zed!",
    "timestamp": "2025-11-17T..."
  },
  "isError": false
}
```

- [ ] Echo tool returns correct text
- [ ] Timestamp is included
- [ ] No errors

#### Test 2: Calculator Tool

**Request (Addition):**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 4,
  "params": {
    "name": "calculator",
    "parameters": {
      "operation": "add",
      "a": 15,
      "b": 27
    }
  }
}
```

**Expected result:**
```json
{
  "result": {
    "operation": "add",
    "a": 15,
    "b": 27,
    "result": 42
  },
  "isError": false
}
```

Test all operations:
- [ ] Add: 15 + 27 = 42
- [ ] Subtract: 50 - 8 = 42
- [ ] Multiply: 6 * 7 = 42
- [ ] Divide: 84 / 2 = 42

Error handling:
- [ ] Division by zero returns error
- [ ] Unknown operation returns error

#### Test 3: Get Time Tool

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 5,
  "params": {
    "name": "get_time",
    "parameters": {
      "timezone": "UTC"
    }
  }
}
```

**Expected result:**
```json
{
  "result": {
    "timezone": "UTC",
    "time": "2025-11-17T...",
    "unix": 1234567890
  },
  "isError": false
}
```

- [ ] Returns current time in ISO format
- [ ] Returns Unix timestamp
- [ ] Defaults to UTC when no timezone specified

#### Test 4: Reverse String Tool

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 6,
  "params": {
    "name": "reverse_string",
    "parameters": {
      "text": "Hello"
    }
  }
}
```

**Expected result:**
```json
{
  "result": {
    "original": "Hello",
    "reversed": "olleH",
    "length": 5
  },
  "isError": false
}
```

- [ ] String is reversed correctly
- [ ] Original string is preserved
- [ ] Length is accurate

### 5. Error Handling

#### Unknown Tool
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 7,
  "params": {
    "name": "nonexistent_tool",
    "parameters": {}
  }
}
```

- [ ] Returns error result with `isError: true`
- [ ] Error message: "Tool 'nonexistent_tool' not found"

#### Missing Required Parameters
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 8,
  "params": {
    "name": "echo",
    "parameters": {}
  }
}
```

- [ ] Tool handles missing parameters gracefully
- [ ] Returns empty or default value (implementation-dependent)

#### Invalid JSON-RPC
```bash
echo 'invalid json' | dotnet run -- --server
```

- [ ] Server returns JSON-RPC parse error
- [ ] Error code: -32700
- [ ] Server continues running

### 6. Session Management

#### Active Session
- [ ] Session is created on `initialize`
- [ ] Session ID is generated
- [ ] Session timeout is set (default: 30 minutes)
- [ ] Pending requests are tracked

#### Graceful Shutdown

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "shutdown",
  "id": 9,
  "params": {
    "reason": "Testing complete"
  }
}
```

**Expected behavior:**
- [ ] Server waits up to 5 seconds for pending requests
- [ ] All pending requests complete or timeout
- [ ] Session transitions to terminated state
- [ ] Success response returned
- [ ] Connection closes cleanly

### 7. Performance Metrics

Use the automated test script and measure:

```bash
cd examples/Andy.Acp.Examples
time ./test-server.sh
```

- [ ] **Startup time**: < 2 seconds
- [ ] **Response latency**: < 100ms per request
- [ ] **Memory usage**: < 50MB
- [ ] **Session cleanup**: No resource leaks

### 8. Stress Testing

Run multiple operations in sequence:

```bash
# Run client test multiple times
for i in {1..10}; do
  dotnet run -- --client | dotnet run -- --server > /dev/null 2>&1
  echo "Run $i completed"
done
```

- [ ] All runs complete successfully
- [ ] No crashes or hangs
- [ ] Memory usage remains stable
- [ ] Sessions clean up properly

## Common Issues and Solutions

### Issue: "Connection refused"
**Solution:**
- Verify `dotnet` is in PATH
- Check absolute paths in settings.json
- Test command manually: `dotnet run --project /path/to/examples/Andy.Acp.Examples -- --server`

### Issue: "Invalid JSON-RPC message"
**Solution:**
- Check that only stderr is used for logging (no stdout pollution)
- Verify message format follows JSON-RPC 2.0 spec
- Use `test-server.sh` to verify protocol flow

### Issue: "Session timeout"
**Solution:**
- Ensure `initialized` notification is sent after `initialize`
- Check session state in server logs
- Verify session timeout setting (default: 30 minutes)

### Issue: "Tool not found"
**Solution:**
- Verify tool name spelling (case-sensitive)
- Check server capabilities in initialize response
- Use `tools/list` to see available tools

### Issue: "Tool execution error"
**Solution:**
- Verify parameter types match JSON schema
- Check required vs optional parameters
- Review tool implementation error handling

## Manual Testing Protocol

1. **Fresh Start**: Close Zed, rebuild example, restart Zed
2. **Connection**: Verify connection establishes within 5 seconds
3. **Discovery**: Check Zed can see all 4 tools
4. **Execution**: Test each tool at least once
5. **Errors**: Test at least one error condition
6. **Shutdown**: Close Zed and verify graceful shutdown

## Automated Testing

Use the provided test script:

```bash
cd examples/Andy.Acp.Examples
./test-server.sh
```

Expected output:
- All protocol methods registered
- Initialize handshake successful
- All tools working (echo, calculator, get_time, reverse_string)
- Shutdown completing successfully

## Test Results Template

```markdown
## Test Results

**Date**: YYYY-MM-DD
**Tester**: [Name]
**Zed Version**: [Version]
**.NET Version**: [Version]
**OS**: [macOS/Linux/Windows version]

### Connection
- [ ] Passes / [ ] Fails
- Notes:

### Tool Discovery
- [ ] Passes / [ ] Fails
- Tools found: [count]
- Notes:

### Tool Execution
- Echo: [ ] Pass / [ ] Fail
- Calculator: [ ] Pass / [ ] Fail
- Get Time: [ ] Pass / [ ] Fail
- Reverse String: [ ] Pass / [ ] Fail

### Error Handling
- [ ] Passes / [ ] Fails
- Notes:

### Performance
- Startup time: [seconds]
- Avg response time: [ms]
- Memory usage: [MB]

### Issues Found
1. [Issue description]
2. [Issue description]

### Overall Status
- [ ] All tests pass
- [ ] Some tests fail
- [ ] Blocker issues found
```

## Success Criteria

For Issue #7 to be considered complete:

- [ ] Connection establishes reliably
- [ ] All 4 tools are discoverable
- [ ] All 4 tools execute correctly
- [ ] Error handling works as expected
- [ ] Session management is stable
- [ ] Performance meets requirements
- [ ] Documentation is complete and accurate
- [ ] No critical bugs

## Next Steps

After completing testing:

1. Document any issues found as GitHub issues
2. Update documentation based on findings
3. Mark Issue #7 as complete if all criteria met
4. Consider performance optimizations if needed
5. Plan for andy-cli integration (full tool library)
