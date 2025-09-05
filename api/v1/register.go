package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
	"k8s.io/apimachinery/pkg/runtime/schema"
)

var (
	// SchemeBuilder initializes a scheme builder
	SchemeBuilder = runtime.NewSchemeBuilder(addKnownTypes)
	// AddToScheme is a global function that registers the types with a scheme
	AddToScheme = SchemeBuilder.AddToScheme
)

// Resource takes an unqualified resource and returns a Group qualified GroupResource
func Resource(resource string) schema.GroupResource {
	return GroupVersion.WithResource(resource).GroupResource()
}

// addKnownTypes adds the set of types defined in this package to the supplied scheme.
func addKnownTypes(scheme *runtime.Scheme) error {
	scheme.AddKnownTypes(GroupVersion,
		&A0Client{},
		&A0ClientList{},
		&A0Connection{},
		&A0ConnectionList{},
		&A0ClientGrant{},
		&A0ClientGrantList{},
		&A0ResourceServer{},
		&A0ResourceServerList{},
		&A0Tenant{},
		&A0TenantList{},
	)
	metav1.AddToGroupVersion(scheme, GroupVersion)
	return nil
}
