/* Copyright © 2021 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.ComponentModel.DataAnnotations;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NodeDeserializers;

namespace Softelvdm.Tools.DeploySite {

    public class ValidatingNodeDeserializer : INodeDeserializer {
        private readonly INodeDeserializer _nodeDeserializer;

        public ValidatingNodeDeserializer(INodeDeserializer nodeDeserializer) {
            _nodeDeserializer = nodeDeserializer;
        }

        public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object> nestedObjectDeserializer, out object value) {
            if (_nodeDeserializer.Deserialize(reader, expectedType, nestedObjectDeserializer, out value)) {
                ValidationContext context = new ValidationContext(value, null, null);
                Validator.ValidateObject(value, context, true);
                return true;
            }
            return false;
        }
    }

    public class Yaml {

        public static IDeserializer GetDeserializer() {

            IDeserializer deserializer = new DeserializerBuilder().WithNodeDeserializer(inner => new ValidatingNodeDeserializer(inner), s => s.InsteadOf<ObjectNodeDeserializer>()).Build();
            return deserializer;
        }
    }
}
