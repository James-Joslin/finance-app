FROM postgres:18-alpine@sha256:d3e1620b530c944afa6e887d22eb899824da68e19c52024bf98f5220c88a65b2

RUN apk add --no-cache python3 py3-pip && python3 -m venv /opt/finova-venv

ENV PATH="/opt/finova-venv/bin:$PATH" \
    PYTHONUNBUFFERED=1 \
    TZ=UTC

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir --requirement requirements.txt

COPY finova_backup.py ./
COPY tests ./tests

USER postgres

ENTRYPOINT ["python", "/app/finova_backup.py"]
CMD ["schedule"]
