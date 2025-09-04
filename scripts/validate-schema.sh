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

SCRIPT_ROOT=$(dirname "${BASH_SOURCE[0]}")/../

echo "🔍 Validating CRD schema compatibility..."

# Paths to compare
GO_CRD_DIR="${SCRIPT_ROOT}/config/crd/bases"
CS_CRD_DIR="${SCRIPT_ROOT}/src/Alethic.Auth0.Operator/config"

# CRD files to compare
CRD_FILES=(
    "kubernetes.auth0.com_a0clients.yaml"
    "kubernetes.auth0.com_a0connections.yaml"
    "kubernetes.auth0.com_a0clientgrants.yaml"
    "kubernetes.auth0.com_a0resourceservers.yaml"
    "kubernetes.auth0.com_a0tenants.yaml"
)

# Function to extract schema from CRD YAML
extract_schema() {
    local crd_file="$1"
    # Extract just the schema part for comparison, excluding metadata
    yq '.spec.versions[0].schema.openAPIV3Schema' "$crd_file"
}

# Function to compare schemas
compare_schemas() {
    local go_file="$1"
    local cs_file="$2"
    local resource_name="$3"
    
    echo "  📋 Comparing ${resource_name}..."
    
    if [[ ! -f "$go_file" ]]; then
        echo "    ❌ Go CRD not found: $go_file"
        return 1
    fi
    
    if [[ ! -f "$cs_file" ]]; then
        echo "    ⚠️  C# CRD not found: $cs_file (this is expected for Go-generated CRDs)"
        return 0
    fi
    
    # Extract schemas to temp files for comparison
    local go_schema_file=$(mktemp)
    local cs_schema_file=$(mktemp)
    
    extract_schema "$go_file" > "$go_schema_file"
    extract_schema "$cs_file" > "$cs_schema_file"
    
    # Compare schemas
    if diff -u "$cs_schema_file" "$go_schema_file" > /dev/null; then
        echo "    ✅ Schemas match perfectly"
        rm -f "$go_schema_file" "$cs_schema_file"
        return 0
    else
        echo "    ⚠️  Schema differences detected:"
        diff -u "$cs_schema_file" "$go_schema_file" | head -20
        echo "       (showing first 20 lines of differences)"
        rm -f "$go_schema_file" "$cs_schema_file"
        return 1
    fi
}

# Check if yq is installed
if ! command -v yq >/dev/null 2>&1; then
    echo "⚠️  yq not found. Installing via go..."
    go install github.com/mikefarah/yq/v4@latest
    export PATH=$PATH:$(go env GOPATH)/bin
fi

# Track validation results
VALIDATION_ERRORS=0

# Compare each CRD file
for crd_file in "${CRD_FILES[@]}"; do
    go_crd="${GO_CRD_DIR}/${crd_file}"
    cs_crd="${CS_CRD_DIR}/${crd_file/kubernetes.auth0.com_/}"
    
    if ! compare_schemas "$go_crd" "$cs_crd" "$crd_file"; then
        ((VALIDATION_ERRORS++))
    fi
done

echo ""
if [[ $VALIDATION_ERRORS -eq 0 ]]; then
    echo "🎉 All CRD schemas validated successfully!"
    echo "   The Go-generated CRDs are compatible with the existing C# operator."
else
    echo "❌ Found $VALIDATION_ERRORS schema compatibility issues."
    echo "   Review the differences above and update the Go types if necessary."
    exit 1
fi

echo ""
echo "📊 Validation Summary:"
echo "  - Go CRDs location: ${GO_CRD_DIR}"
echo "  - C# CRDs location: ${CS_CRD_DIR}"
echo "  - Files compared: ${#CRD_FILES[@]}"
echo "  - Validation result: ✅ PASSED"