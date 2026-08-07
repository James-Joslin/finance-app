FROM python:3.13-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /migrations

COPY requirements.txt .
RUN pip install --no-cache-dir --requirement requirements.txt

COPY alembic.ini env.py script.py.mako ./
COPY versions ./versions

ENTRYPOINT ["alembic"]
CMD ["upgrade", "head"]
