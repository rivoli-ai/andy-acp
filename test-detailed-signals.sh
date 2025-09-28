#!/bin/bash

echo "Testing detailed signal handling..."

# Test SIGINT (Ctrl+C)
echo "Test 1: Testing SIGINT (Ctrl+C)"
dotnet run --project examples/Andy.Acp.Examples -- --server &
SERVER_PID=$!
sleep 1
echo "Sending SIGINT to PID $SERVER_PID"
kill -INT $SERVER_PID
wait $SERVER_PID
echo "SIGINT test completed"
echo ""

# Test SIGTERM
echo "Test 2: Testing SIGTERM"
dotnet run --project examples/Andy.Acp.Examples -- --server &
SERVER_PID=$!
sleep 1
echo "Sending SIGTERM to PID $SERVER_PID"
kill -TERM $SERVER_PID
wait $SERVER_PID
echo "SIGTERM test completed"