using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Alethic.Auth0.Operator.Controllers;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    [TestClass]
    public class V1TenantEntityControllerTests
    {

        [TestMethod]
        public void AreValuesEqual_OrderInsensitive_EnabledConnections_SameOrder_ReturnsTrue()
        {
            var connection1 = new Hashtable { { "id", "con_123" } };
            var connection2 = new Hashtable { { "id", "con_456" } };
            
            var leftArray = new[] { connection1, connection2 };
            var rightArray = new[] { connection1, connection2 };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsTrue(result, "Arrays with same connections in same order should be equal");
        }

        [TestMethod]
        public void AreValuesEqual_OrderInsensitive_EnabledConnections_DifferentOrder_ReturnsTrue()
        {
            var connection1 = new Hashtable { { "id", "con_123" } };
            var connection2 = new Hashtable { { "id", "con_456" } };
            
            var leftArray = new[] { connection1, connection2 };
            var rightArray = new[] { connection2, connection1 };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsTrue(result, "Arrays with same connections in different order should be equal");
        }

        [TestMethod]
        public void AreValuesEqual_OrderInsensitive_EnabledConnections_DifferentConnections_ReturnsFalse()
        {
            var connection1 = new Hashtable { { "id", "con_123" } };
            var connection2 = new Hashtable { { "id", "con_456" } };
            var connection3 = new Hashtable { { "id", "con_789" } };
            
            var leftArray = new[] { connection1, connection2 };
            var rightArray = new[] { connection1, connection3 };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsFalse(result, "Arrays with different connections should not be equal");
        }

        [TestMethod]
        public void AreValuesEqual_OrderSensitive_RegularArrays_DifferentOrder_ReturnsFalse()
        {
            var leftArray = new[] { "string1", "string2" };
            var rightArray = new[] { "string2", "string1" };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsFalse(result, "Regular arrays should be order-sensitive");
        }

        [TestMethod]
        public void AreValuesEqual_OrderSensitive_RegularArrays_SameOrder_ReturnsTrue()
        {
            var leftArray = new[] { "string1", "string2" };
            var rightArray = new[] { "string1", "string2" };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsTrue(result, "Regular arrays with same order should be equal");
        }

        [TestMethod]
        public void AreValuesEqual_EnabledConnections_ComplexObjects_DifferentOrder_ReturnsTrue()
        {
            var connection1 = new Hashtable 
            { 
                { "id", "con_123" },
                { "name", "Google OAuth2" },
                { "strategy", "google-oauth2" }
            };
            var connection2 = new Hashtable 
            { 
                { "id", "con_456" },
                { "name", "Database Connection" },
                { "strategy", "auth0" }
            };
            
            var leftArray = new[] { connection1, connection2 };
            var rightArray = new[] { connection2, connection1 };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsTrue(result, "Complex connection objects in different order should be equal");
        }

        [TestMethod]
        public void AreValuesEqual_EmptyArrays_ReturnsTrue()
        {
            var leftArray = new object[0];
            var rightArray = new object[0];

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsTrue(result, "Empty arrays should be equal");
        }

        [TestMethod]
        public void AreValuesEqual_DifferentLengths_ReturnsFalse()
        {
            var connection1 = new Hashtable { { "id", "con_123" } };
            var connection2 = new Hashtable { { "id", "con_456" } };
            
            var leftArray = new[] { connection1 };
            var rightArray = new[] { connection1, connection2 }; // Different length

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsFalse(result, "Arrays with different lengths should not be equal");
        }

        [TestMethod]
        public void AreValuesEqual_MixedObjects_WithAndWithoutId_OrderSensitive()
        {
            var connectionWithId = new Hashtable { { "id", "con_123" } };
            var objectWithoutId = new Hashtable { { "name", "some_object" } };
            
            var leftArray = new[] { connectionWithId, objectWithoutId };
            var rightArray = new[] { objectWithoutId, connectionWithId };

            var result = V1ClientController.AreValuesEqual(leftArray, rightArray);

            Assert.IsFalse(result, "Mixed arrays (some with id, some without) should be order-sensitive");
        }
    }
}