FROM php:8.4-cli-alpine

# cURL уже включён в этот официальный образ PHP.
WORKDIR /app
COPY . /app
ENV API_BASE_URL=http://api:8080
EXPOSE 8080
CMD ["php", "-d", "display_errors=0", "-d", "log_errors=1", "-S", "0.0.0.0:8080", "-t", "/app"]
