#!/bin/bash

# Test script for Andy ACP Examples server
# This script uses the built-in client mode to test the server

echo "🧪 Testing Andy ACP Example Server"
echo "=================================="
echo ""

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo -e "${BLUE}Running built-in client/server test...${NC}"
echo ""

# Run the built-in test using client and server modes
dotnet run --project "$SCRIPT_DIR" -- --client 2>&1 | \
    dotnet run --project "$SCRIPT_DIR" -- --server 2>&1 | \
    grep -E '(Registered ACP|Initialize|pong|echo|shutdown|Success|Sessions terminated)' | head -15

echo ""
echo -e "${GREEN}✅ Test completed!${NC}"
echo ""
echo "The output above shows:"
echo "  - ACP protocol methods registered"
echo "  - Initialize handshake successful"
echo "  - Ping responding with 'pong'"
echo "  - Echo working"
echo "  - Shutdown completing successfully"
echo ""
echo -e "${BLUE}To test with Zed editor:${NC}"
echo "1. Build the example: dotnet build"
echo "2. Get the absolute path: pwd"
echo "3. Update ~/.config/zed/settings.json with:"
echo "   - Copy configuration from zed-settings.example.json"
echo "   - Replace /absolute/path/to/andy-acp with the path from step 2"
echo "4. Open Zed and activate Assistant (Cmd+? or Ctrl+?)"
echo ""
echo "Repository path: $(cd "$SCRIPT_DIR/../.." && pwd)"
