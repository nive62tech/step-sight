from alembic import context
from app.config import get_settings
from app.persistence import Base
from sqlalchemy import create_engine

config = context.config
target_metadata = Base.metadata
url = get_settings().database_url
if context.is_offline_mode():
    context.configure(url=url, target_metadata=target_metadata, literal_binds=True)
    with context.begin_transaction():
        context.run_migrations()
else:
    with create_engine(url, hide_parameters=True).connect() as connection:
        context.configure(connection=connection, target_metadata=target_metadata)
        with context.begin_transaction():
            context.run_migrations()
