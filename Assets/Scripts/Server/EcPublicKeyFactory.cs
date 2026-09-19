using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace CubeArena.Server
{
    public static class EcPublicKeyFactory
    {
        // NIST P-256, matching the backend's TicketOptions (see docs/ARCHITECTURE.md).
        public static ECPublicKeyParameters FromRawCoordinates(byte[] x, byte[] y)
        {
            var curve = SecNamedCurves.GetByName("secp256r1");
            var domainParameters = new Org.BouncyCastle.Crypto.Parameters.ECDomainParameters(
                curve.Curve, curve.G, curve.N, curve.H);
            var point = curve.Curve.CreatePoint(new BigInteger(1, x), new BigInteger(1, y));
            return new ECPublicKeyParameters(point, domainParameters);
        }
    }
}
