from functools import lru_cache
from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    app_name: str = "AlSaqr Cloud ERP API"
    app_env: str = "development"
    database_url: str = "postgresql+asyncpg://alsaqr_app:local_app_change_this_password@db:5432/alsaqr_saas"
    migration_database_url: str | None = "postgresql+asyncpg://alsaqr_migrator:local_migrator_change_this_password@db:5432/alsaqr_saas"
    jwt_secret_key: str = "local-only-secret-key-change-this-to-a-random-value-at-least-32-chars"
    jwt_access_token_minutes: int = 20
    cors_origins: str = "http://localhost:5173,http://127.0.0.1:5173"
    openai_api_key: str | None = None
    openai_vision_model: str = "gpt-4o"
    max_invoice_upload_bytes: int = 10 * 1024 * 1024
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    @field_validator("jwt_secret_key")
    @classmethod
    def require_long_jwt_secret(cls, value: str) -> str:
        if len(value) < 32:
            raise ValueError("JWT_SECRET_KEY must contain at least 32 characters")
        return value

    @property
    def cors_origin_list(self) -> list[str]:
        return [item.strip() for item in self.cors_origins.split(",") if item.strip()]


@lru_cache
def get_settings() -> Settings:
    settings = Settings()
    if settings.app_env.lower() in {"production", "prod"}:
        if settings.jwt_secret_key.startswith("local-only-"):
            raise RuntimeError("Set a unique JWT_SECRET_KEY before production startup")
        if settings.cors_origin_list == ["http://localhost:5173"]:
            raise RuntimeError("Configure production CORS_ORIGINS before production startup")
    return settings
