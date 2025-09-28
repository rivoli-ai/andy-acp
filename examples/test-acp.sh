#!/bin/bash

# Test script to demonstrate ACP communication

echo "Testing ACP Protocol Communication"
echo "=================================="
echo

# Create a test message in ACP format
create_acp_message() {
    local json="$1"
    local len=${#json}
    echo -ne "Content-Length: $len\r\n\r\n$json"
}

# Test 1: Send a simple message to the server
echo "Test 1: Sending a simple message to server..."
message='{"id":1,"method":"test","params":{"value":"hello"},"jsonrpc":"2.0"}'
response=$(create_acp_message "$message" | dotnet run --project Andy.Acp.Examples -- --server 2>/dev/null | tail -n +5)
echo "Response: $response"
echo

# Test 2: Send multiple messages
echo "Test 2: Sending multiple messages..."
(
    create_acp_message '{"id":1,"method":"ping","jsonrpc":"2.0"}'
    create_acp_message '{"id":2,"method":"echo","params":{"text":"world"},"jsonrpc":"2.0"}'
) | dotnet run --project Andy.Acp.Examples -- --server 2>/dev/null | tail -n +5
echo

echo "Tests complete!"