#!/usr/bin/env bash

# Copyright 2025 SeatGeek.
# 
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
# 
#     http://www.apache.org/licenses/LICENSE-2.0
# 
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

set -o errexit
set -o nounset
set -o pipefail

VERSION=${1:-""}

if [ -z "$VERSION" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 v1.2.3"
    exit 1
fi

# Validate version format (should be vX.Y.Z)
if [[ ! $VERSION =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Version must be in format vX.Y.Z (e.g., v1.2.3)"
    exit 1
fi

echo "🚀 Starting release process for ${VERSION}..."

# Check if we're on a clean working tree
if ! git diff-index --quiet HEAD --; then
    echo "Error: Working tree is not clean. Commit or stash your changes first."
    exit 1
fi

# Run pre-release checks
echo "📋 Running pre-release checks..."

echo "  - Generating code and CRDs..."
make generate

echo "  - Running tests..."
make test

echo "  - Running linting..."
make lint

echo "  - Verifying generated code is up to date..."
make verify

echo "✅ Pre-release checks passed!"

# Check if tag already exists
if git rev-parse "${VERSION}" >/dev/null 2>&1; then
    echo "Error: Tag ${VERSION} already exists"
    exit 1
fi

# Create and push the tag
echo "🏷️  Creating and pushing tag ${VERSION}..."
git tag "${VERSION}"
git push origin "${VERSION}"

# Create GitHub release
echo "📦 Creating GitHub release..."
if command -v gh >/dev/null 2>&1; then
    gh release create "${VERSION}" \
        --title "${VERSION}" \
        --notes "Release ${VERSION}" \
        --latest
    echo "✅ GitHub release created: https://github.com/seatgeek/auth0-operator/releases/tag/${VERSION}"
else
    echo "⚠️  GitHub CLI (gh) not found. Please create the release manually:"
    echo "   Visit: https://github.com/seatgeek/auth0-operator/releases/new"
    echo "   Tag: ${VERSION}"
    echo "   Title: ${VERSION}"
fi

echo "🎉 Release ${VERSION} completed successfully!"
echo ""
echo "📝 Next steps:"
echo "  1. Update CHANGELOG.md with release notes"
echo "  2. Verify the release at: https://github.com/seatgeek/auth0-operator/releases"
echo "  3. Test consumption: go get github.com/seatgeek/auth0-operator@${VERSION}"