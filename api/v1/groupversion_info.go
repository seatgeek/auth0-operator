// Package v1 contains API Schema definitions for the kubernetes.auth0.com v1 API group
// +kubebuilder:object:generate=true
// +groupName=kubernetes.auth0.com
package v1

import (
	"k8s.io/apimachinery/pkg/runtime/schema"
)

var (
	// GroupVersion is group version used to register these objects
	GroupVersion = schema.GroupVersion{Group: "kubernetes.auth0.com", Version: "v1"}
)
