# Build Configuration
SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.DEFAULT_GOAL := help

# Go Configuration
GO_VERSION := 1.22
GOOS := $(shell go env GOOS)
GOARCH := $(shell go env GOARCH)

# Kubernetes Configuration
CRD_OPTIONS ?= "crd"
CONTROLLER_GEN = $(shell pwd)/bin/controller-gen

# Paths
API_DIR := ./api/v1
CRD_OUTPUT_DIR := ./config/crd/bases
GENERATED_DIR := ./pkg/generated

.PHONY: help
help: ## Display this help
	@awk 'BEGIN {FS = ":.*##"; printf "\nUsage:\n  make \033[36m<target>\033[0m\n"} /^[a-zA-Z_0-9-]+:.*?##/ { printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2 } /^##@/ { printf "\n\033[1m%s\033[0m\n", substr($$0, 5) } ' $(MAKEFILE_LIST)

##@ Development

.PHONY: generate
generate: controller-gen ## Generate CRDs and deep copy code
	$(CONTROLLER_GEN) object:headerFile="hack/boilerplate.go.txt" paths="./api/..."
	$(CONTROLLER_GEN) crd:allowDangerousTypes=true paths="./api/..." output:crd:artifacts:config=$(CRD_OUTPUT_DIR)

.PHONY: manifests
manifests: controller-gen ## Generate CRD manifests
	$(CONTROLLER_GEN) crd:allowDangerousTypes=true paths="./api/..." output:crd:artifacts:config=$(CRD_OUTPUT_DIR)

.PHONY: fmt
fmt: ## Run go fmt against code
	go fmt ./...

.PHONY: vet
vet: ## Run go vet against code
	go vet ./...

.PHONY: test
test: fmt vet ## Run tests
	go test ./... -coverprofile cover.out

.PHONY: build
build: generate fmt vet ## Build the project
	go build -v ./...

.PHONY: lint
lint: ## Run linter (requires golangci-lint to be installed)
	@if command -v golangci-lint > /dev/null 2>&1; then \
		golangci-lint run; \
	else \
		echo "golangci-lint not found. Install it with: go install github.com/golangci/golangci-lint/cmd/golangci-lint@latest"; \
	fi

.PHONY: verify
verify: ## Verify that generated code is up to date
	@git diff --exit-code || (echo "Generated code is out of date. Run 'make generate' and commit the changes."; exit 1)

.PHONY: tidy
tidy: ## Run go mod tidy
	go mod tidy

##@ Code Generation

.PHONY: codegen
codegen: ## Generate all code (clients, informers, listers)
	./hack/update-codegen.sh

.PHONY: verify-codegen
verify-codegen: ## Verify generated code is up to date
	./hack/verify-codegen.sh

##@ Schema Validation

.PHONY: validate-schema
validate-schema: generate ## Validate generated CRDs against existing ones
	@echo "Comparing generated CRDs with existing C# operator CRDs..."
	@./scripts/validate-schema.sh

##@ Tools

CONTROLLER_GEN = $(shell pwd)/bin/controller-gen
.PHONY: controller-gen
controller-gen: ## Download controller-gen locally if necessary
	@if ! test -f $(CONTROLLER_GEN); then \
		echo "Downloading controller-gen..."; \
		mkdir -p $(shell dirname $(CONTROLLER_GEN)); \
		GOBIN=$(shell pwd)/bin go install sigs.k8s.io/controller-tools/cmd/controller-gen@v0.17.2; \
	fi

.PHONY: clean
clean: ## Clean generated files and build artifacts
	rm -rf $(GENERATED_DIR)
	rm -rf $(CRD_OUTPUT_DIR)
	rm -rf ./bin
	go clean -cache

##@ Release

.PHONY: release
release: ## Create a new release (requires VERSION environment variable)
	@if [ -z "$(VERSION)" ]; then \
		echo "VERSION environment variable is required. Usage: make release VERSION=v1.0.0"; \
		exit 1; \
	fi
	@./scripts/release.sh $(VERSION)

.PHONY: tag
tag: ## Create and push a git tag (requires VERSION environment variable)
	@if [ -z "$(VERSION)" ]; then \
		echo "VERSION environment variable is required. Usage: make tag VERSION=v1.0.0"; \
		exit 1; \
	fi
	git tag $(VERSION)
	git push fork $(VERSION)