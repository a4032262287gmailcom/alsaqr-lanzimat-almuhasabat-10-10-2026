import base64
from datetime import datetime, timezone
from decimal import Decimal


def _tlv(tag: int, value: str) -> bytes:
    encoded = value.encode("utf-8")
    if len(encoded) > 255:
        raise ValueError("ZATCA Phase 1 TLV fields cannot exceed 255 UTF-8 bytes")
    return bytes((tag, len(encoded))) + encoded


def build_phase_one_qr(
    seller_name: str,
    seller_tax_number: str,
    issued_at: datetime,
    invoice_total: Decimal,
    vat_total: Decimal,
) -> str:
    """Build the five mandatory Phase 1 TLV tags. This is not Phase 2 signing or clearance."""
    if issued_at.tzinfo is None:
        issued_at = issued_at.replace(tzinfo=timezone.utc)
    timestamp = issued_at.isoformat(timespec="seconds")
    payload = b"".join((
        _tlv(1, seller_name),
        _tlv(2, seller_tax_number),
        _tlv(3, timestamp),
        _tlv(4, f"{invoice_total:.2f}"),
        _tlv(5, f"{vat_total:.2f}"),
    ))
    return base64.b64encode(payload).decode("ascii")
