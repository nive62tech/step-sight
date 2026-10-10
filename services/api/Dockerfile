FROM ghcr.io/astral-sh/uv:0.8.22 AS uv
FROM python:3.12.13-slim-bookworm
COPY --from=uv /uv /usr/local/bin/uv
WORKDIR /app
COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project && useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin guide
COPY services/api/ ./services/api/
COPY lessons/ ./lessons/
COPY scripts/reconcile-deletions.py ./scripts/reconcile-deletions.py
ENV PYTHONPATH=/app/services/api PYTHONDONTWRITEBYTECODE=1 UV_NO_CACHE=1
USER 10001:10001
EXPOSE 8000
CMD [".venv/bin/uvicorn", "app.main:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000", "--workers", "1", "--no-access-log", "--no-proxy-headers"]
