`hospital-code.json` contains a synthetic registration URL and its QR module matrix,
generated with QRCoder's ECC Q setting, matching `QrCodeService`. The matrix includes
the quiet zone. The Node tests render it into RGBA pixels to exercise the actual
vendored jsQR decoder and camera polling flow without a physical camera.

The code is invented and is not associated with a patient or database record.
