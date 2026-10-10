import io
import warnings

from fastapi import HTTPException, Request
from PIL import Image
from python_multipart import MultipartParser
from python_multipart.multipart import parse_options_header

MAX_IMAGE_BYTES = 5 * 1024 * 1024
MAX_REQUEST_BYTES = 6 * 1024 * 1024
MAX_PIXELS = 16_000_000
MAX_METADATA_BYTES = 64 * 1024


async def bounded_body(request: Request, limit: int) -> bytes:
    size = 0
    chunks = []
    async for chunk in request.stream():
        size += len(chunk)
        if size > limit:
            raise HTTPException(413, "request exceeds size limit")
        chunks.append(chunk)
    return b"".join(chunks)


async def observation_upload(request: Request) -> tuple[bytes, bytes]:
    content_type, options = parse_options_header(request.headers.get("content-type", ""))
    boundary = options.get(b"boundary")
    if content_type != b"multipart/form-data" or not boundary or len(boundary) > 200:
        raise HTTPException(415, "multipart metadata and screenshot required")
    # Deliberately avoid UploadFile/Starlette's SpooledTemporaryFile path.
    parts: dict[str, bytes] = {}
    headers: dict[bytes, bytes] = {}
    field = bytearray()
    value = bytearray()
    data = bytearray()
    part_name = ""
    part_count = 0

    def begin():
        nonlocal part_count
        part_count += 1
        if part_count > 2:
            raise ValueError("too many parts")
        headers.clear()
        data.clear()

    def on_header_field(chunk, start, end):
        field.extend(chunk[start:end])
        if len(field) > 200:
            raise ValueError("header size")

    def on_header_value(chunk, start, end):
        value.extend(chunk[start:end])
        if len(value) > 1000:
            raise ValueError("header size")

    def header_end():
        headers[bytes(field).lower()] = bytes(value)
        field.clear()
        value.clear()

    def headers_finished():
        nonlocal part_name
        _, disposition = parse_options_header(headers.get(b"content-disposition", b""))
        part_name = disposition.get(b"name", b"").decode("ascii")
        if part_name not in {"metadata", "screenshot"} or part_name in parts:
            raise ValueError("unexpected or repeated part")

    def part_data(chunk, start, end):
        data.extend(chunk[start:end])
        limit = MAX_IMAGE_BYTES if part_name == "screenshot" else MAX_METADATA_BYTES
        if len(data) > limit:
            raise HTTPException(413, "part exceeds size limit")

    def part_end():
        parts[part_name] = bytes(data)

    parser = MultipartParser(boundary, {
        "on_part_begin": begin, "on_header_field": on_header_field, "on_header_value": on_header_value,
        "on_header_end": header_end, "on_headers_finished": headers_finished,
        "on_part_data": part_data, "on_part_end": part_end,
    }, max_size=MAX_REQUEST_BYTES)
    try:
        parser.write(await bounded_body(request, MAX_REQUEST_BYTES))
        parser.finalize()
        if set(parts) != {"metadata", "screenshot"}:
            raise ValueError("missing part")
        return parts["metadata"], parts["screenshot"]
    except (ValueError, UnicodeError):
        raise HTTPException(400, "invalid multipart observation") from None


def validate_image(data: bytes, expected_width: int, expected_height: int):
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(io.BytesIO(data)) as image:
                if image.format not in {"PNG", "JPEG"} or getattr(image, "n_frames", 1) != 1:
                    raise ValueError("unsupported image")
                width, height = image.size
                if width * height > MAX_PIXELS or (width, height) != (expected_width, expected_height):
                    raise ValueError("image dimensions")
                image.verify()
    except Exception:
        raise HTTPException(400, "invalid screenshot or dimensions") from None


def validate_audio(data: bytes):
    import wave

    try:
        with wave.open(io.BytesIO(data), "rb") as audio:
            if audio.getnchannels() != 1 or audio.getsampwidth() != 2 or audio.getframerate() != 16000:
                raise ValueError("audio format")
            if not 0 < audio.getnframes() <= 30 * 16000:
                raise ValueError("audio length")
            if len(audio.readframes(audio.getnframes())) != audio.getnframes() * 2:
                raise ValueError("truncated audio")
    except Exception:
        raise HTTPException(400, "audio must be mono 16 kHz 16-bit WAV, at most 30 seconds") from None
