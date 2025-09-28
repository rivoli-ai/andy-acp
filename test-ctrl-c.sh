#!/bin/bash

echo "Testing Ctrl+C handling..."
echo "Starting server in background..."

# Start server and get its PID
dotnet run --project examples/Andy.Acp.Examples -- --server &
SERVER_PID=$!

echo "Server PID: $SERVER_PID"

# Wait a moment for server to start
sleep 2

echo "Sending SIGINT (Ctrl+C) to server..."
kill -INT $SERVER_PID

# Wait to see if it shuts down gracefully
sleep 3

# Check if process is still running
if kill -0 $SERVER_PID 2>/dev/null; then
    echo "Server did not respond to SIGINT, force killing..."
    kill -KILL $SERVER_PID
    echo "FAILED: Server did not handle Ctrl+C properly"
    exit 1
else
    echo "SUCCESS: Server handled Ctrl+C and exited gracefully"
    exit 0
fi