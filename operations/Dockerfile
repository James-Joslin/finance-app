FROM postgres:17-alpine@sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73

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
