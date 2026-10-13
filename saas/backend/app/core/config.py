from functools import lru_cache
from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    app_env: str = "development"
    app_name: str = "AlSaqr Cloud ERP"
    database_url: str = "postgresql+asyncpg://alsaqr_app:local_dev_change_this_password@localhost:5433/alsaqr_saas"
    jwt_secret_key: str = "local-only-secret-key-change-this-to-a-random-value-at-least-32-chars"
    jwt_access_token_minutes: int = 20
    cors_origins: str = "http://localhost:5173,http://127.0.0.1:5173"
    max_request_body_bytes: int = 15 * 1024 * 1024

    @field_validator("jwt_secret_key")
    @classmethod
    def validate_jwt_secret(cls, value: str) -> str:
        if len(value) < 32:
            raise ValueError("JWT_SECRET_KEY must be at least 32 characters.")
        return value

    @property
    def allowed_origins(self) -> list[str]:
        return [origin.strip() for origin in self.cors_origins.split(",") if origin.strip()]

    @property
    def is_production(self) -> bool:
        return self.app_env.lower() == "production"


@lru_cache
def get_settings() -> Settings:
    settings = Settings()
    if settings.is_production and settings.jwt_secret_key.startswith("local-only-"):
        raise ValueError("Production deployments must replace the development JWT secret.")
    return settings


settings = get_settings()
